# CTM3 — Регистрация `IClayTreeMutations` и разрешение в компоненте

## Цель

Дать приложению способ зарегистрировать свою реализацию `IClayTreeMutations` и научить компонент
получать её опционально: если функции изменения выключены — сервис не требуется; если включены —
его отсутствие это конфигурационная ошибка с понятным сообщением.

## Шаг 1 — файл `Clayzor.Lib.Web.Controls/Services/ServiceCollectionExtensions.cs`

В метод `AddClayTree` НИЧЕГО про мутации не добавляем автоматически (реализация — прикладная).
Вместо этого добавить отдельный публичный метод-помощник в этот же класс, ПОСЛЕ `AddClayTree`:

```csharp
    /// <summary>
    /// Регистрирует реализацию <see cref="IClayTreeMutations"/> для операций изменения данных дерева
    /// (drag-and-drop, редактирование, добавление, удаление). Вызывается приложением, если для
    /// какого-либо дерева включены соответствующие опции.
    /// </summary>
    public static IServiceCollection AddClayTreeMutations<TImpl>(this IServiceCollection services)
        where TImpl : class, IClayTreeMutations
    {
        services.AddScoped<IClayTreeMutations, TImpl>();
        return services;
    }
```

Проверить, что в начале файла есть `using Clayzor.Lib.Web.Controls.Components.Tree;` — если нет,
добавить.

## Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.razor.cs`

**2.1.** Добавить опциональный inject рядом с остальными inject-полями (`Db`, `StateStore`,
`NavigationManager`):

```csharp
    // Опционально: нужен только когда включены функции изменения данных.
    [Inject] private IServiceProvider Services { get; set; } = default!;
```

Не инжектить `IClayTreeMutations` напрямую как обязательный — иначе деревья без изменения данных
потребуют регистрации сервиса. Резолвим по требованию через `IServiceProvider`.

**2.2.** Добавить приватное свойство ленивого доступа с понятной ошибкой:

```csharp
    private IClayTreeMutations? _mutationsCached;

    /// <summary>
    /// Сервис изменения данных. Резолвится по требованию. Если функции изменения включены,
    /// но сервис не зарегистрирован — кидает информативное исключение.
    /// </summary>
    private IClayTreeMutations Mutations
    {
        get
        {
            _mutationsCached ??= Services.GetService(typeof(IClayTreeMutations)) as IClayTreeMutations;
            if (_mutationsCached is null)
                throw new InvalidOperationException(
                    "Для операций изменения данных дерева не зарегистрирован IClayTreeMutations. " +
                    "Вызовите services.AddClayTreeMutations<ВашаРеализация>() в Program.cs, " +
                    "либо отключите EnableDragDrop/EnableEdit/EnableAddChild/EnableDelete.");
            return _mutationsCached;
        }
    }
```

## Критерии приёмки

- Проект собирается.
- Дерево без включённых функций изменения работает без регистрации `IClayTreeMutations`
  (свойство `Mutations` не вызывается).
- При включённой функции и отсутствии регистрации — при первой операции понятное исключение
  (текст на русском, с указанием, что делать).
