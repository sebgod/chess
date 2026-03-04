namespace Chess.Lib.UI;

[Flags]
public enum UIResponse
{
    None,
    NeedsRefresh       = 0b0001,
    NeedsPromotionType = 0b0010,
    IsUpdate           = 0b0100,
    NeedsPiecePlacement = 0b1000,
}
