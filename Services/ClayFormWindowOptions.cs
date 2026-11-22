namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Параметры размера и положения отдельного окна формы.</summary>
public sealed class ClayFormWindowOptions
{
    /// <summary>Ширина окна в пикселях.</summary>
    public int Width { get; init; } = 1100;

    /// <summary>Высота окна в пикселях.</summary>
    public int Height { get; init; } = 760;

    /// <summary>Координата левого края окна.</summary>
    public int Left { get; init; } = 120;

    /// <summary>Координата верхнего края окна.</summary>
    public int Top { get; init; } = 80;

    /// <summary>Разрешает изменение размера окна.</summary>
    public bool Resizable { get; init; } = true;
}
