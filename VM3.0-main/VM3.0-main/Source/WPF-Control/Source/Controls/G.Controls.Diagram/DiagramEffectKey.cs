namespace G.Controls.Diagram;

public class DiagramEffectKey
{
    public static ComponentResourceKey IsDragEnter => new ComponentResourceKey(typeof(DiagramEffectKey), "S.EffectShadow.IsDragEnter");
    public static ComponentResourceKey IsCanDrop => new ComponentResourceKey(typeof(DiagramEffectKey), "S.EffectShadow.IsCanDrop");
    public static ComponentResourceKey IsSelected => new ComponentResourceKey(typeof(DiagramEffectKey), "S.EffectShadow.IsSelected");
    public static ComponentResourceKey MouseOver => new ComponentResourceKey(typeof(DiagramEffectKey), "S.EffectShadow.MouseOver");
    public static ComponentResourceKey IsDragging => new ComponentResourceKey(typeof(DiagramEffectKey), "S.EffectShadow.IsDragging");

}
