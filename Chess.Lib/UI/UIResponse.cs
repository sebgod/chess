namespace Chess.Lib.UI;

[Flags]
public enum UIResponse
{
    None,
    NeedsRefresh        = 0b00001,
    NeedsPromotionType  = 0b00010,
    IsUpdate            = 0b00100,
    NeedsPiecePlacement = 0b01000,
    NeedsReset          = 0b10000,
}
