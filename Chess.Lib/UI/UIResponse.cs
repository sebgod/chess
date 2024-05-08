namespace Chess.Lib.UI;

[Flags]
public enum UIResponse
{
    None,
    NeedsRefresh       = 0b001,
    NeedsPromotionType = 0b010, 
    IsUpdate           = 0b100,
}
