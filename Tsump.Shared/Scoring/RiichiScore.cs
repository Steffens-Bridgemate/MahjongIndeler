namespace Tsump.Scoring;

public enum RiichiWin { Ron, Tsumo }

// Outcome of a Riichi score lookup. Exactly one payment shape is populated:
//   Ron            -> RonPayment (the discarder pays this; == Total)
//   Dealer tsumo   -> TsumoEach  (each of the three others pays this)
//   Non-dealer tsumo -> TsumoNonDealer (each non-dealer) + TsumoDealer (the dealer)
public record RiichiResult(
    bool Valid, bool IsTsumo, bool IsDealer, int Total,
    int? RonPayment,
    int? TsumoEach,
    int? TsumoNonDealer,
    int? TsumoDealer,
    string LimitName);

// Standard Riichi scoring tables. Source: Downloads/Riichi scores/*.png.
// Fan 1-4 are keyed by fu (null cell = combination does not exist).
// Fan 5+ are limit hands and ignore fu.
// Kiriage mangan: 30fu/4han and 60fu/3han are rounded UP to mangan (8000/12000 ron,
// 2000/4000 & 4000 tsumo) for both ron and tsumo — the club's house rule, baked into the cells below.
public static class RiichiScore
{
    public static readonly int[] FuValues = { 20, 25, 30, 40, 50, 60, 70 };
    public const int MinFan = 1;
    public const int MaxFan = 11;          // 12 = Yakuman, handled via the yakuman flag
    public const int LimitFan = 5;         // fan >= this ignores fu (limit hands)

    private static int FuIndex(int fu) => Array.IndexOf(FuValues, fu);

    // The two fan/fu combinations the kiriage house rule rounds up to mangan (see table comment).
    // They carry a "Kiriage Mangan" label, shown like the Mangan/Baiman limit badges.
    private static bool IsKiriageMangan(int fan, int fu) => (fan == 4 && fu == 30) || (fan == 3 && fu == 60);

    // --- Fan 1-4 lookups. Outer index = fu (FuValues order), inner index = fan-1. null = invalid. ---

    private static readonly int?[][] RonDealer =
    {
        new int?[] { null, null,  null,  null  },  // 20
        new int?[] { null, 2400,  4800,  9600  },  // 25
        new int?[] { 1500, 2900,  5800,  12000 },  // 30  (4 han: kiriage mangan)
        new int?[] { 2000, 3900,  7700,  12000 },  // 40
        new int?[] { 2400, 4800,  9600,  12000 },  // 50
        new int?[] { 2900, 5800,  12000, 12000 },  // 60  (3 han: kiriage mangan)
        new int?[] { 3400, 6800,  12000, 12000 },  // 70
    };

    private static readonly int?[][] RonNonDealer =
    {
        new int?[] { null, null, null, null },  // 20
        new int?[] { null, 1600, 3200, 6400 },  // 25
        new int?[] { 1000, 2000, 3900, 8000 },  // 30  (4 han: kiriage mangan)
        new int?[] { 1300, 2600, 5200, 8000 },  // 40
        new int?[] { 1600, 3200, 6400, 8000 },  // 50
        new int?[] { 2000, 3900, 8000, 8000 },  // 60  (3 han: kiriage mangan)
        new int?[] { 2300, 4500, 8000, 8000 },  // 70
    };

    // Dealer tsumo: amount each of the three others pays.
    private static readonly int?[][] TsumoDealerEach =
    {
        new int?[] { null, 700,  1300, 2600 },  // 20
        new int?[] { null, null, 1600, 3200 },  // 25
        new int?[] { 500,  1000, 2000, 4000 },  // 30  (4 han: kiriage mangan)
        new int?[] { 700,  1300, 2600, 4000 },  // 40
        new int?[] { 800,  1600, 3200, 4000 },  // 50
        new int?[] { 1000, 2000, 4000, 4000 },  // 60  (3 han: kiriage mangan)
        new int?[] { 1200, 2300, 4000, 4000 },  // 70
    };

    // Non-dealer tsumo: amount each non-dealer pays.
    private static readonly int?[][] TsumoNonDealerEach =
    {
        new int?[] { null, 400,  700,  1300 },  // 20
        new int?[] { null, null, 800,  1600 },  // 25
        new int?[] { 300,  500,  1000, 2000 },  // 30
        new int?[] { 400,  700,  1300, 2000 },  // 40
        new int?[] { 400,  800,  1600, 2000 },  // 50
        new int?[] { 500,  1000, 2000, 2000 },  // 60
        new int?[] { 600,  1200, 2000, 2000 },  // 70
    };

    // Non-dealer tsumo: amount the dealer pays.
    private static readonly int?[][] TsumoNonDealerDealerPay =
    {
        new int?[] { null, 700,  1300, 2600 },  // 20
        new int?[] { null, null, 1600, 3200 },  // 25
        new int?[] { 500,  1000, 2000, 4000 },  // 30  (4 han: kiriage mangan)
        new int?[] { 700,  1300, 2600, 4000 },  // 40
        new int?[] { 800,  1600, 3200, 4000 },  // 50
        new int?[] { 1000, 2000, 4000, 4000 },  // 60  (3 han: kiriage mangan)
        new int?[] { 1200, 2300, 4000, 4000 },  // 70
    };

    // --- Limit hands (fan 5+). Index by tier. ---
    private record Limit(string Name, int DealerTsumoEach, int DealerRon,
                         int NonDealerTsumoEach, int NonDealerTsumoDealer, int NonDealerRon);

    private static readonly Limit Mangan    = new("Mangan",    4000,  12000, 2000, 4000,  8000);
    private static readonly Limit Haneman   = new("Haneman",   6000,  18000, 3000, 6000,  12000);
    private static readonly Limit Baiman    = new("Baiman",    8000,  24000, 4000, 8000,  16000);
    private static readonly Limit Sanbaiman = new("Sanbaiman", 12000, 36000, 6000, 12000, 24000);
    private static readonly Limit Yakuman   = new("Yakuman",   16000, 48000, 8000, 16000, 32000);

    private static Limit LimitFor(int fan) => fan switch
    {
        5 => Mangan,
        6 or 7 => Haneman,
        >= 8 and <= 10 => Baiman,
        _ => Sanbaiman,   // 11 (and any higher counted fan)
    };

    // Fu values that produce a valid score for the given win type and fan (fan 1-4 only).
    // Used by the wizard to offer only legal fu.
    public static IReadOnlyList<int> ValidFu(RiichiWin win, bool isDealer, int fan)
    {
        if (fan is < 1 or >= LimitFan)
            return Array.Empty<int>();

        var table = win == RiichiWin.Ron
            ? (isDealer ? RonDealer : RonNonDealer)
            : (isDealer ? TsumoDealerEach : TsumoNonDealerEach);

        var result = new List<int>();
        for (int i = 0; i < FuValues.Length; i++)
            if (table[i][fan - 1] is not null)
                result.Add(FuValues[i]);
        return result;
    }

    public static RiichiResult Calculate(RiichiWin win, bool isDealer, int fan, bool yakuman, int? fu)
    {
        bool isTsumo = win == RiichiWin.Tsumo;

        // Yakuman / limit hands ignore fu.
        if (yakuman || fan >= LimitFan)
        {
            var limit = yakuman ? Yakuman : LimitFor(fan);
            return BuildLimitResult(limit, isTsumo, isDealer);
        }

        if (fan is < 1 or > 4 || fu is null || FuIndex(fu.Value) < 0)
            return Invalid(isTsumo, isDealer);

        int fi = FuIndex(fu.Value);
        int fanIdx = fan - 1;
        string kiriage = IsKiriageMangan(fan, fu.Value) ? "Kiriage Mangan" : "";

        if (win == RiichiWin.Ron)
        {
            var cell = (isDealer ? RonDealer : RonNonDealer)[fi][fanIdx];
            if (cell is null) return Invalid(isTsumo, isDealer);
            return new RiichiResult(true, false, isDealer, cell.Value, cell.Value, null, null, null, kiriage);
        }

        if (isDealer)
        {
            var each = TsumoDealerEach[fi][fanIdx];
            if (each is null) return Invalid(isTsumo, isDealer);
            return new RiichiResult(true, true, true, each.Value * 3, null, each.Value, null, null, kiriage);
        }
        else
        {
            var each = TsumoNonDealerEach[fi][fanIdx];
            var dealerPay = TsumoNonDealerDealerPay[fi][fanIdx];
            if (each is null || dealerPay is null) return Invalid(isTsumo, isDealer);
            int total = each.Value * 2 + dealerPay.Value;
            return new RiichiResult(true, true, false, total, null, null, each.Value, dealerPay.Value, kiriage);
        }
    }

    private static RiichiResult BuildLimitResult(Limit limit, bool isTsumo, bool isDealer)
    {
        if (!isTsumo)
        {
            int ron = isDealer ? limit.DealerRon : limit.NonDealerRon;
            return new RiichiResult(true, false, isDealer, ron, ron, null, null, null, limit.Name);
        }

        if (isDealer)
            return new RiichiResult(true, true, true, limit.DealerTsumoEach * 3, null,
                limit.DealerTsumoEach, null, null, limit.Name);

        int total = limit.NonDealerTsumoEach * 2 + limit.NonDealerTsumoDealer;
        return new RiichiResult(true, true, false, total, null, null,
            limit.NonDealerTsumoEach, limit.NonDealerTsumoDealer, limit.Name);
    }

    private static RiichiResult Invalid(bool isTsumo, bool isDealer) =>
        new(false, isTsumo, isDealer, 0, null, null, null, null, "");
}
