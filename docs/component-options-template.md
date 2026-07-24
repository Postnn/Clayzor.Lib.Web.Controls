# Шаблон: Options для нового компонента

Пример для гипотетического компонента `ClayFoo<T>` с настройками `ClayFooOptions`. Копировать и адаптировать под конкретный компонент.

## 1. Класс настроек (`Components/Foo/ClayFooOptions.cs`)

```csharp
namespace Clayzor.Lib.Web.Controls.Components.Foo;

/// <summary>
/// Настройки одного экземпляра <see cref="ClayFoo{T}"/> на странице.
/// <para>
/// Объект создаётся страницей ОДИН РАЗ и хранится в поле, а не собирается выражением
/// в разметке: компонент сравнивает ссылку на параметр, и новый объект на каждый рендер
/// приводит к лишним пересчётам.
/// </para>
/// </summary>
public sealed class ClayFooOptions
{
    /// <summary>Значения по умолчанию. Экземпляр общий и не должен изменяться.</summary>
    public static ClayFooOptions Defaults { get; } = new();

    // ── Блок 1 ───────────────────────────────────────────────────────

    /// <summary>Описание свойства.</summary>
    public string Prop1 { get; set; } = "default";

    // ── Блок 2 ───────────────────────────────────────────────────────

    /// <summary>Описание свойства.</summary>
    public bool Prop2 { get; set; } = true;
}
```

## 2. Параметр и поле в компоненте

```csharp
// В Parameters:
[Parameter] public ClayFooOptions? Options { get; set; }

// Поле:
private ClayFooOptions _opt = new();

// Разрешение:
private ClayFooOptions ResolveOptions() => Options ?? new ClayFooOptions();

protected override void OnInitialized()
{
    _opt = ResolveOptions();
    // ... остальная инициализация
}

protected override void OnParametersSet()
{
    _opt = ResolveOptions();
    // ...
}
```

## 3. Страница-потребитель

```razor
<ClayFoo TEntity="IMyRow"
         Options="_fooOptions"
         Items="_rows"
         Loading="_loading" />

@code {
    private ClayFooOptions _fooOptions = null!;

    protected override void OnInitialized()
    {
        _fooOptions = new ClayFooOptions
        {
            Prop1 = "value",
            Prop2 = false,
        };
    }
}
```

## 4. Тест дефолтов

```csharp
public class ClayFooOptionsTests
{
    [Fact]
    public void Defaults_HaveExpectedValues()
    {
        var d = new ClayFooOptions();
        Assert.Equal("default", d.Prop1);
        Assert.True(d.Prop2);
    }

    [Fact]
    public void Defaults_StaticProperty_ReturnsSameInstance()
    {
        Assert.Same(ClayFooOptions.Defaults, ClayFooOptions.Defaults);
    }
}
```
