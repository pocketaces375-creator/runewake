using System;
using Godot;
using Godot.Collections;

namespace Runewake.Client;

/// <summary>
/// FABLE-061: a thin C# face on the GodotGooglePlayBilling Android plugin (addons/GodotGooglePlayBilling,
/// Billing Library 9). The plugin is a JNI singleton that only exists in an Android build exported with the
/// plugin enabled; everywhere else Available is false and every call does nothing.
///
/// Calls and signals mirror the plugin's own GDScript wrapper (BillingClient.gd):
///   startConnection → connected / connect_error
///   queryProductDetails(ids, "inapp") → query_product_details_response {response_code, product_details[]}
///   queryPurchases("inapp", false)    → query_purchases_response {response_code, purchases[]}
///   purchase(id, "", "", false)       → on_purchase_updated {response_code, purchases[]}
///   consumePurchase(token)            → consume_purchase_response {response_code, token}
/// A purchase dictionary: purchase_token, product_ids[], purchase_state (1 purchased, 2 pending), order_id …
/// </summary>
public static class PlayBilling
{
    public const string SingletonName = "GodotGooglePlayBilling";
    public const int ResponseOk = 0;
    public const int ResponseUserCanceled = 1;
    public const int StatePurchased = 1;
    public const int StatePending = 2;

    private static GodotObject? _s;

    public static event Action? Connected;
    public static event Action<int, string>? ConnectFailed;
    public static event Action<Dictionary>? ProductDetails;
    public static event Action<Dictionary>? PurchasesUpdated;
    public static event Action<Dictionary>? PurchasesQueried;
    public static event Action<Dictionary>? ConsumeDone;

    public static bool Available => Godot.Engine.HasSingleton(SingletonName);

    /// <summary>Find the plugin and wire its signals. Safe to call more than once.</summary>
    public static bool Init()
    {
        if (_s != null) return true;
        if (!Available) return false;
        try
        {
            _s = Godot.Engine.GetSingleton(SingletonName);
            _s.Call("initPlugin");
            _s.Connect("connected", Callable.From(() => Connected?.Invoke()));
            _s.Connect("disconnected", Callable.From(() => GD.Print("[PlayBilling] disconnected")));
            _s.Connect("connect_error", Callable.From<int, string>((c, m) => ConnectFailed?.Invoke(c, m)));
            _s.Connect("query_product_details_response", Callable.From<Dictionary>(d => ProductDetails?.Invoke(d)));
            _s.Connect("query_purchases_response", Callable.From<Dictionary>(d => PurchasesQueried?.Invoke(d)));
            _s.Connect("on_purchase_updated", Callable.From<Dictionary>(d => PurchasesUpdated?.Invoke(d)));
            _s.Connect("consume_purchase_response", Callable.From<Dictionary>(d => ConsumeDone?.Invoke(d)));
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayBilling] init failed: {ex.Message}");
            _s = null;
            return false;
        }
    }

    public static bool IsReady => _s != null && _s.Call("isReady").AsBool();
    public static void StartConnection() => _s?.Call("startConnection");
    public static void QueryProducts(string[] ids) => _s?.Call("queryProductDetails", ids, "inapp");
    public static void QueryPurchases() => _s?.Call("queryPurchases", "inapp", false);
    public static void Consume(string token) => _s?.Call("consumePurchase", token);

    /// <summary>Ties the purchase to this Supabase account (the server refuses it on any other account).</summary>
    public static void SetAccount(string userId) => _s?.Call("setObfuscatedAccountId", userId);

    /// <summary>Opens Google's purchase sheet. The result arrives later through PurchasesUpdated.</summary>
    public static Dictionary Purchase(string productId)
    {
        if (_s == null) return new Dictionary { { "response_code", -1 }, { "debug_message", "billing not available" } };
        var v = _s.Call("purchase", productId, "", "", false);
        return v.VariantType == Variant.Type.Dictionary ? v.AsGodotDictionary() : new Dictionary();
    }

    // ── reading the plugin's dictionaries ────────────────────────────────

    public static int Code(Dictionary d) => d.ContainsKey("response_code") ? d["response_code"].AsInt32() : -1;

    public static Godot.Collections.Array Purchases(Dictionary d) =>
        d.ContainsKey("purchases") && d["purchases"].VariantType == Variant.Type.Array ? d["purchases"].AsGodotArray() : new Godot.Collections.Array();

    public static string FirstProduct(Dictionary purchase)
    {
        if (!purchase.ContainsKey("product_ids")) return "";
        var v = purchase["product_ids"];
        if (v.VariantType == Variant.Type.PackedStringArray) { var a = v.AsStringArray(); return a.Length > 0 ? a[0] : ""; }
        if (v.VariantType == Variant.Type.Array) { var a = v.AsGodotArray(); return a.Count > 0 ? a[0].AsString() : ""; }
        return "";
    }

    /// <summary>product id → Google's localized price ("$0.25", "0,29 €") from a product-details reply.</summary>
    public static System.Collections.Generic.Dictionary<string, string> Prices(Dictionary reply)
    {
        var map = new System.Collections.Generic.Dictionary<string, string>();
        if (!reply.ContainsKey("product_details") || reply["product_details"].VariantType != Variant.Type.Array) return map;
        foreach (var item in reply["product_details"].AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var pd = item.AsGodotDictionary();
            string id = pd.ContainsKey("product_id") ? pd["product_id"].AsString() : "";
            if (id == "" || !pd.ContainsKey("one_time_purchase_offer_details_list")) continue;
            var offers = pd["one_time_purchase_offer_details_list"];
            if (offers.VariantType != Variant.Type.Array || offers.AsGodotArray().Count == 0) continue;
            var first = offers.AsGodotArray()[0];
            if (first.VariantType == Variant.Type.Dictionary && first.AsGodotDictionary().ContainsKey("formatted_price"))
                map[id] = first.AsGodotDictionary()["formatted_price"].AsString();
        }
        return map;
    }
}
