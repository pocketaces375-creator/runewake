using System.Net.Http;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Coop;
using Runewake.Engine.Engine;
using Runewake.Engine.Supabase;

namespace Runewake.Sim;

/// <summary>
/// FABLE-038: a seat at the table for a machine. Signs in (anonymously, or with a saved
/// session), joins a lobby by its code, marks itself ready, waits for the host to start, and
/// then plays the duel with GreedyBot through the same move log a phone uses. Trikzos: "maybe
/// we can make an account for you or my Hermes agent to join" — this is that account.
///
///   dotnet run --project sim -- pvp-bot --code ABC123 [--name "Fable"] [--root .] [--think 1.5]
///                                        [--session ~/.runewake/bot_session.json]
///
/// Needs SUPABASE_URL and SUPABASE_ANON_KEY in the environment (or --url / --key).
/// Plain .NET HttpClient is fine here — this runs on a PC, not on the phone.
/// </summary>
public static class PvpBot
{
    public static int Run(string[] args)
    {
        string root = ".", code = "", name = "Fable (bot)", url = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
        string key = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ?? "";
        string sessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".runewake", "bot_session.json");
        string cls = "battlemage";
        double think = 1.5;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--code" when i + 1 < args.Length: code = args[++i].Trim().ToUpperInvariant(); break;
                case "--name" when i + 1 < args.Length: name = args[++i]; break;
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--url" when i + 1 < args.Length: url = args[++i]; break;
                case "--key" when i + 1 < args.Length: key = args[++i]; break;
                case "--class" when i + 1 < args.Length: cls = args[++i]; break;
                case "--think" when i + 1 < args.Length: think = double.Parse(args[++i]); break;
                case "--session" when i + 1 < args.Length: sessionPath = args[++i]; break;
            }
        }
        if (code.Length != 6) { Console.Error.WriteLine("pvp-bot: --code XXXXXX is required (the 6-letter lobby code)"); return 2; }
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key)) { Console.Error.WriteLine("pvp-bot: set SUPABASE_URL and SUPABASE_ANON_KEY (or --url/--key)"); return 2; }
        return RunAsync(root, code, name, url, key, cls, think, sessionPath).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string root, string code, string name, string url, string key, string cls, double think, string sessionPath)
    {
        // content — FABLE-042: the phone's own copy (client/content, what the APK ships), so the bot
        // builds exactly the duel the phone builds. The repo's top-level content/ is a separate
        // copy that can drift (it has extra artifact variants the phone doesn't).
        var content = Path.Combine(root, "client", "content");
        if (!Directory.Exists(Path.Combine(content, "cards"))) content = Path.Combine(root, "content");
        var cards = Directory.GetFiles(Path.Combine(content, "cards"), "*.json").SelectMany(CardLoader.LoadPack).ToList();
        CardRegistry.Clear(); CardRegistry.RegisterRange(cards);
        var artPath = Path.Combine(content, "artifacts", "launch_artifacts.json");
        if (File.Exists(artPath)) ArtifactLoader.LoadPack(artPath);
        var variants = Path.Combine(content, "artifacts", "variants");
        if (Directory.Exists(variants)) ArtifactLoader.LoadAllVariants(variants);
        using var starterDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "client", "content", "decks", "starter_decks.json")));
        var starters = starterDoc.RootElement.GetProperty("starters").EnumerateArray()
            .ToDictionary(s => s.GetProperty("class_id").GetString()!, s => s.GetProperty("cards").EnumerateArray().Select(x => x.GetString()!).ToList());
        if (!starters.TryGetValue(cls, out var deck)) { cls = starters.Keys.First(); deck = starters[cls]; }
        Console.WriteLine($"[bot] {name}: {cls}, {deck.Count}-card starter deck");

        var config = new SupabaseConfig { Url = url, AnonKey = key };
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var auth = new SupabaseAuth(config, http);
        var session = await Session(auth, sessionPath);
        if (session == null) return 1;
        Console.WriteLine($"[bot] signed in as {session.DisplayLabel()}");

        var sync = new OnlinePlaySync(config, http);
        var f = await sync.Find(session, code);
        if (!f.ok || f.found == null) { Console.Error.WriteLine($"[bot] no open lobby has code {code}: {f.error}"); return 1; }
        if (f.found.Kind != "pvp1v1") { Console.Error.WriteLine($"[bot] that lobby is {f.found.Kind}; the bot only plays 1v1 duels"); return 1; }
        var j = await sync.Join(session, f.found.Id, name, cls, deck);
        if (!j.ok) { Console.Error.WriteLine($"[bot] could not join: {j.error}"); return 1; }
        await sync.SetReady(session, f.found.Id, true);
        Console.WriteLine($"[bot] joined {f.found.HostName}'s lobby in seat {j.seat}. Waiting for the host to start…");

        OnlinePlaySync.Lobby? lobby = null;
        for (int waited = 0; waited < 600; waited += 2)
        {
            var l = await sync.GetLobby(session, f.found.Id);
            if (l.ok && l.lobby != null)
            {
                lobby = l.lobby;
                if (lobby.State == "running" && lobby.Seed != null) break;
                if (lobby.State is "abandoned" or "done") { Console.WriteLine("[bot] the lobby closed"); return 0; }
            }
            await Task.Delay(2000);
        }
        if (lobby == null || lobby.State != "running" || lobby.Seed is not long seed) { Console.WriteLine("[bot] gave up waiting"); return 1; }

        var seats = lobby.Members.Where(m => m.Status != "left").OrderBy(m => m.Seat)
            .Select(m => new SeatConfig { Seat = m.Seat, DisplayName = m.DisplayName, ClassId = m.ClassId, Deck = new List<string>(m.Deck), Artifacts = ArtifactRegistry.DefaultLoadoutFor(m.ClassId) })
            .ToList();
        int me = lobby.Mine!.Seat;
        var duel = new PvpDuel((ulong)seed, seats[0], seats[1]);
        var ps = new PvpSession(duel, me);
        var outbound = new Queue<string>();
        ps.Outbound += json => outbound.Enqueue(json);
        ps.Desynced += (n, s) => Console.WriteLine(n == 0
            ? "[bot] VERSION MISMATCH — the phone built a different duel from this lobby. Update the bot's repo to the phone's build."
            : $"[bot] DESYNC after move {n} (seat {s})");
        ps.Hello();
        var bot = new GreedyBot();
        int seq = 0; long cursor = 0;
        var rnd = new Random();
        Console.WriteLine($"[bot] duel started, seed {seed}. I am seat {me} vs {seats[1 - me].DisplayName}.");

        while (!ps.State.IsGameOver)
        {
            // send
            while (outbound.Count > 0)
            {
                var r = await sync.PostMove(session, lobby.Id, seq, outbound.Peek());
                if (!r.ok) { Console.Error.WriteLine($"[bot] post_move: {r.error}"); await Task.Delay(1500); continue; }
                outbound.Dequeue(); seq++;
            }
            // receive
            var m = await sync.MovesSince(session, lobby.Id, cursor);
            if (m.ok)
                foreach (var row in m.rows)
                {
                    cursor = Math.Max(cursor, row.Id);
                    if (row.Seat != me) ps.Receive(row.Payload);
                }
            // act
            if (ps.IsMyTurn)
            {
                await Task.Delay((int)(think * 1000 * (0.7 + rnd.NextDouble() * 0.6)));
                var a = bot.ChooseAction(ps.State, me) ?? new EndTurnAction { PlayerIndex = me };
                if (!ps.Local(a, out var err))
                {
                    Console.Error.WriteLine($"[bot] my move was refused ({err}) — ending turn");
                    ps.Local(new EndTurnAction { PlayerIndex = me }, out _);
                }
                else Console.WriteLine($"[bot] turn {ps.State.TurnNumber}: {Describe(a)}");
            }
            else await Task.Delay(1200);
        }
        while (outbound.Count > 0)
        {
            var r = await sync.PostMove(session, lobby.Id, seq, outbound.Peek());
            if (!r.ok) break;
            outbound.Dequeue(); seq++;
        }
        bool won = ps.State.WinnerIndex == me;
        Console.WriteLine(won ? "[bot] I won. gg" : $"[bot] {seats[1 - me].DisplayName} won. gg");
        await sync.Finish(session, lobby.Id, new { winner_seat = ps.State.WinnerIndex, reported_by = me, moves = ps.MovesApplied });
        await sync.Leave(session, lobby.Id);
        return 0;
    }

    private static async Task<SupabaseSession?> Session(SupabaseAuth auth, string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<SupabaseSession>(File.ReadAllText(path));
                if (saved != null && saved.IsValid)
                {
                    var r = await auth.Refresh(saved);
                    if (r.Ok && r.Session != null) { Save(path, r.Session); return r.Session; }
                }
            }
            var a = await auth.SignInAnonymously();
            if (!a.Ok || a.Session == null) { Console.Error.WriteLine($"[bot] sign-in failed: {a.Error}"); return null; }
            Save(path, a.Session);
            return a.Session;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[bot] sign-in threw: {ex.Message}"); return null; }
    }

    private static void Save(string path, SupabaseSession s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(s));
        }
        catch (Exception ex) { Console.Error.WriteLine($"[bot] session not saved: {ex.Message}"); }
    }

    private static string Describe(GameAction a) => a switch
    {
        PlayCardAction p => $"play card #{p.CardInstanceId} → lane {p.LaneIndex}",
        AttackAction at => $"attack lane {at.SourceLane} → {at.TargetLane}",
        EndTurnAction => "end turn",
        _ => a.GetType().Name,
    };
}
