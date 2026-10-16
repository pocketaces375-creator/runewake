using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;

namespace Runewake.Client;

/// <summary>
/// FABLE-061: everything between the Buy button and unopened packs in the save, alive for the whole run
/// (a purchase can complete after the shop is closed, or on the next launch).
///
///   Buy(id)        → Google's purchase sheet (PlayBilling)
///   purchase done  → Deliver: server verifies with Google and rolls the packs → stored in the save →
///                    told the server "received" → PacksArrived
///   on connect     → any purchase Google still holds unconsumed is delivered again (the server never
///                    rolls a token twice), and packs the server still owes this account are picked up
///   Report(...)    → cards found on the map / in the Tower, for the 90-day pack rule
///
/// Without the billing plugin (desktop, sandbox, an APK built without it) packs cannot be bought; a DEBUG
/// build offers free preview packs instead so the opening can be tried.
/// </summary>
public static class PackStore
{
    public static event Action<List<PendingPack>>? PacksArrived;
    public static event Action<string>? Notice;
    public static event Action? Changed;

    public static PackSync.PackStatus? Status { get; private set; }
    public static readonly Dictionary<string, string> Prices = new();
    public static bool Busy { get; private set; }

    private static bool _init;
    private static readonly HashSet<string> _delivering = new();

    public static bool BillingReady => PlayBilling.Available && PlayBilling.IsReady;

    /// <summary>Free test packs: only in a debug build (or with --packs-preview) and only without real billing.</summary>
    public static bool PreviewMode =>
        !PlayBilling.Available && (OS.IsDebugBuild() || System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--packs-preview") >= 0);

    private static SyncManager? Sm()
    {
        var sm = CampaignContext.SyncManager;
        return sm != null && GodotObject.IsInstanceValid(sm) && sm.IsConfigured ? sm : null;
    }

    private static PackSync? Sync() => Sm() is { } sm ? new PackSync(sm.Config, Http.Create(25)) : null;

    public static void Init()
    {
        if (_init) return;
        _init = true;
        if (!PlayBilling.Init()) { GD.Print("[PackStore] no billing plugin — " + (PreviewMode ? "preview packs only" : "packs cannot be bought in this build")); return; }
        PlayBilling.Connected += () =>
        {
            GD.Print("[PackStore] billing connected");
            PlayBilling.QueryProducts(Packs.Products.Select(p => p.Id).ToArray());
            PlayBilling.QueryPurchases();
            Changed?.Invoke();
        };
        PlayBilling.ConnectFailed += (c, m) => { GD.Print($"[PackStore] billing connect failed {c}: {m}"); Changed?.Invoke(); };
        PlayBilling.ProductDetails += d =>
        {
            foreach (var kv in PlayBilling.Prices(d)) Prices[kv.Key] = kv.Value;
            Changed?.Invoke();
        };
        PlayBilling.PurchasesUpdated += d => OnPurchases(d, fromQuery: false);
        PlayBilling.PurchasesQueried += d => OnPurchases(d, fromQuery: true);
        PlayBilling.StartConnection();
    }

    public static string PriceOf(PackProduct p) => Prices.TryGetValue(p.Id, out var s) && s.Length > 0 ? s : p.FallbackPrice;

    /// <summary>The shop's numbers + any packs the server still owes. Call when the shop opens.</summary>
    public static async Task Refresh()
    {
        try
        {
            var sync = Sync();
            var sm = Sm();
            if (sync == null || sm?.Session?.IsValid != true) { Status = null; Changed?.Invoke(); return; }
            Status = await sync.Status(sm.Session!);
            await PickUpOwed();
            if (BillingReady) PlayBilling.QueryPurchases();
            else if (PlayBilling.Available) PlayBilling.StartConnection();
        }
        catch (Exception ex) { GD.Print($"[PackStore] refresh failed: {ex.Message}"); }
        Changed?.Invoke();
    }

    /// <summary>Why the Buy buttons are off, or null when a purchase can start.</summary>
    public static string? CannotBuyReason()
    {
        if (PreviewMode) return null;
        if (!PlayBilling.Available) return "Packs are sold through Google Play — they arrive with the Play Store version.";
        var sm = Sm();
        if (sm == null) return "The shop is not set up in this build.";
        if (!sm.IsLinked) return "Sign in to an account first (Account, top right of the title) — your packs are kept on it.";
        if (Status == null) return "Can't reach the shop right now — check your connection.";
        if (!Status.CanSell)
            return Status.NextCardAt is DateTimeOffset at
                ? $"Packs open on {at.ToLocalTime():MMMM d} — only cards found 90+ days ago go in packs."
                : "Packs aren't open yet — only cards found 90+ days ago go in packs.";
        if (!BillingReady) return "Connecting to Google Play…";
        if (Busy) return "Finishing your last purchase…";
        return null;
    }

    public static void Buy(string productId)
    {
        var product = Packs.Product(productId);
        if (product == null) return;
        if (PreviewMode) { GivePreview(product); return; }
        var why = CannotBuyReason();
        if (why != null) { Notice?.Invoke(why); return; }
        PlayBilling.SetAccount(Sm()!.Session!.UserId);
        var r = PlayBilling.Purchase(productId);
        int code = PlayBilling.Code(r);
        if (code != PlayBilling.ResponseOk)
            Notice?.Invoke("Google Play couldn't open the purchase. Try again in a moment.");
    }

    private static void OnPurchases(Godot.Collections.Dictionary d, bool fromQuery)
    {
        int code = PlayBilling.Code(d);
        if (code == PlayBilling.ResponseUserCanceled) { Notice?.Invoke("Purchase canceled — nothing was charged."); return; }
        if (code != PlayBilling.ResponseOk) { if (!fromQuery) Notice?.Invoke($"Google Play said no ({code}). Nothing was charged."); return; }
        foreach (var item in PlayBilling.Purchases(d))
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var p = item.AsGodotDictionary();
            string token = p.ContainsKey("purchase_token") ? p["purchase_token"].AsString() : "";
            string product = PlayBilling.FirstProduct(p);
            int state = p.ContainsKey("purchase_state") ? p["purchase_state"].AsInt32() : 0;
            if (token == "" || Packs.Product(product) == null) continue;
            if (state == PlayBilling.StatePending) { if (!fromQuery) Notice?.Invoke("Payment pending — your packs arrive as soon as it completes."); continue; }
            if (state == PlayBilling.StatePurchased) _ = Deliver(product, token);
        }
    }

    /// <summary>Server verifies with Google and rolls; the packs go into the save. Safe to repeat for a token.</summary>
    private static async Task Deliver(string product, string token)
    {
        if (!_delivering.Add(token)) return;
        Busy = true; Changed?.Invoke();
        try
        {
            var sync = Sync(); var sm = Sm();
            if (sync == null || sm?.Session?.IsValid != true) { Notice?.Invoke("Your purchase is safe — sign in and open the shop to collect it."); return; }
            var r = await sync.Verify(sm.Session!, product, token);
            if (r.Pending) { Notice?.Invoke(r.Error); return; }
            if (!r.Ok)
            {
                Notice?.Invoke(r.Retry ? "Your purchase is safe — it will be delivered next time the shop opens." : r.Error);
                return;
            }
            var added = Store(r.Packs);
            await sync.MarkReceived(sm.Session!, r.Packs.Select(p => p.Id).ToList());
            if (!r.Consumed) PlayBilling.Consume(token);
            if (added.Count > 0) PacksArrived?.Invoke(added);
        }
        catch (Exception ex) { GD.PrintErr($"[PackStore] deliver failed: {ex.Message}"); Notice?.Invoke("Your purchase is safe — it will be delivered next time the shop opens."); }
        finally { _delivering.Remove(token); Busy = false; Changed?.Invoke(); }
    }

    private static async Task PickUpOwed()
    {
        var sync = Sync(); var sm = Sm();
        if (sync == null || sm?.Session?.IsValid != true) return;
        var (ok, packs, _) = await sync.Owed(sm.Session!);
        if (!ok || packs.Count == 0) return;
        var added = Store(packs);
        await sync.MarkReceived(sm.Session!, packs.Select(p => p.Id).ToList());
        if (added.Count > 0) PacksArrived?.Invoke(added);
    }

    private static List<PendingPack> Store(List<PendingPack> packs)
    {
        var added = Packs.Receive(CampaignContext.Progression, packs);
        try { CampaignContext.SaveManager.Save(); } catch (Exception ex) { GD.PrintErr($"[PackStore] save failed: {ex.Message}"); }
        GD.Print($"[PackStore] stored {added.Count} new pack(s), {CampaignContext.Progression.PendingPacks.Count} unopened");
        return added;
    }

    private static void GivePreview(PackProduct product)
    {
        var never = new HashSet<string>(CampaignContext.LostRelicIndex.Values.Select(r => r.CardId));
        var rng = new Random();
        var packs = Enumerable.Range(0, product.Packs).Select(_ => Packs.PreviewRoll(CardRegistry.GetAll(), never, rng)).ToList();
        var added = Store(packs);
        Notice?.Invoke($"Preview: {product.Packs} free test pack{(product.Packs == 1 ? "" : "s")} (debug builds only — no money involved).");
        PacksArrived?.Invoke(added);
        Changed?.Invoke();
    }

    /// <summary>Open one unopened pack into the collection and save.</summary>
    public static PendingPack? Open(string packId)
    {
        var pack = Packs.Open(CampaignContext.Progression, packId);
        if (pack != null) { try { CampaignContext.SaveManager.Save(); } catch { } }
        Changed?.Invoke();
        return pack;
    }

    /// <summary>
    /// Cards the player just found by beating a map place ("map") or a Tower place ("tower"). The first time
    /// anyone ever finds a card starts its 90 days; fire-and-forget, offline it is simply skipped.
    /// </summary>
    public static async void Report(IEnumerable<string> cardIds, string source)
    {
        try
        {
            var ids = cardIds.Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(80).ToList();
            var sync = Sync(); var sm = Sm();
            if (ids.Count == 0 || sync == null || sm?.Session?.IsValid != true) return;
            bool ok = await sync.Discover(sm.Session!, ids, source);
            GD.Print($"[PackStore] reported {ids.Count} {source} card(s): {(ok ? "ok" : "failed")}");
        }
        catch (Exception ex) { GD.Print($"[PackStore] report failed: {ex.Message}"); }
    }
}
