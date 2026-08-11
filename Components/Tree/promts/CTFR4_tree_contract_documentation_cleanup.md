# CTFR4 --- Финальная синхронизация документации mutation-контракта `ClayTree`

## Контекст

Репозиторий: `Postnn/Clayzor.Lib.Web.Controls`.

После CTM/CTMF текущая реализация `ClayTree` использует
`ClayTreeOptions.TableName` как явный target для встроенного
`ClaySqlTreeMutations`.

Однако в XML-документации остались формулировки старого контракта:

-   `IClayTreeMutations` утверждает, что целевой объект запросов --- тот
    же, что `ClayTreeOptions.SelectSql`;
-   примеры SQL используют формулировку `<SelectSql-объект>`;
-   в `ClayTreeView.razor.cs` комментарий mutation service ссылается на
    `ClayTreeOptions.TargetObject`, хотя актуальное свойство называется
    `TableName`.

Такая документация опасна для последующей агентной разработки: она
противоречит фактическому API.

## Задача

Провести узкую финальную синхронизацию документации mutation API с
текущей реализацией, не меняя runtime-поведение.

## Требования

1.  Изучи актуальные:

    -   `Components/Tree/ClayTreeOptions.cs`;
    -   `Components/Tree/IClayTreeMutations.cs`;
    -   `Components/Tree/ClaySqlTreeMutations.cs`;
    -   `Components/Tree/ClayTreeView.razor.cs`;
    -   `Components/Tree/AGENTS.md`;
    -   `docs/clay-tree-view.md`;
    -   завершённые CTM/CTMF prompts, если они доступны в репозитории.

2.  Сначала установи фактический текущий контракт по коду. Не исправляй
    код под старую документацию.

3.  Зафиксируй однозначно:

    -   `SelectSql` --- источник чтения дерева;
    -   `TableName` --- mutation target встроенного
        `ClaySqlTreeMutations`;
    -   `TableName` может обозначать таблицу или допустимый writable SQL
        object согласно фактическому контракту проекта;
    -   при отсутствии `TableName` используется DI `IClayTreeMutations`,
        если именно так работает актуальный код;
    -   `Schema` определяет имена колонок, используемые встроенными
        мутациями.

4.  Удали/исправь ссылки на несуществующий
    `ClayTreeOptions.TargetObject`.

5.  Исправь XML examples `IClayTreeMutations`, чтобы они не утверждали,
    что write target обязательно равен `SelectSql`.

6.  Проверь документацию Edit/Add/Delete/DnD и убедись, что нигде не
    осталась старая неоднозначность `SelectSql == mutation target`.

7.  Не переименовывай `TableName`.

8.  Не вводи новый `TargetObject`.

9.  Не меняй runtime code, кроме очевидных XML/doc comments. Если
    обнаружишь реальное runtime-противоречие, не исправляй его скрытно в
    этой задаче: зафиксируй отдельно в отчёте.

## Дополнительная проверка

Выполни repository search минимум по:

``` text
TargetObject
SelectSql-объект
SelectSql object
TableName
IClayTreeMutations
ClaySqlTreeMutations
```

Просмотри найденные места вручную: не делай слепую массовую замену.

## Тесты и сборка

Поскольку задача документационная, новые runtime tests не обязательны.

Но после изменений:

1.  собери проект;
2.  убедись, что XML `<see cref="..."/>` ссылки валидны;
3.  запусти существующие tests, если это стандартный и недорогой шаг
    проекта.

## Приёмка

В финальном отчёте:

-   перечисли изменённые файлы;
-   кратко сформулируй итоговый read/write contract;
-   перечисли найденные и исправленные устаревшие термины;
-   отдельно подтверди, что runtime behavior не менялся.
