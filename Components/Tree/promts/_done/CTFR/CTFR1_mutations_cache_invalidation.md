# CTFR1 --- Инвалидация кэша `IClayTreeMutations` при смене настроек `ClayTreeView`

## Контекст

Репозиторий: `Postnn/Clayzor.Lib.Web.Controls`.

Компонент: `Components/Tree/ClayTreeView`.

После финального ревью текущего `ClayTree` обнаружен lifecycle-дефект:
`ClayTreeView` кэширует экземпляр `IClayTreeMutations` в
`_mutationsCached`, однако при изменении параметров компонента кэш не
инвалидируется.

Сейчас `Mutations` лениво создаёт `ClaySqlTreeMutations` на основании
текущих:

-   `Options.TableName`;
-   `Options.Schema`;
-   результата `ResolveDb()`, который зависит от
    `Options.ConnectionStringName`.

При этом `OnParametersSetAsync()` умеет обнаруживать смену части
настроек источника данных, пересоздавать `_source` и `_dataSource`, а
`_mutationsCached` остаётся прежним.

Особенно опасен сценарий смены `ConnectionStringName`: `ResolveDb()`
может уничтожить старый `_customDb` и создать новый `DbManager`, в то
время как ранее созданный `ClaySqlTreeMutations` продолжит хранить
ссылку на старый `DbManager`.

## Задача

Исправить lifecycle `IClayTreeMutations`, чтобы после изменения
mutation-relevant параметров компонент никогда не использовал устаревший
mutation service.

## Требования

1.  Изучи актуальные:

    -   `Components/Tree/ClayTreeView.razor.cs`;
    -   `Components/Tree/ClaySqlTreeMutations.cs`;
    -   `Components/Tree/IClayTreeMutations.cs`;
    -   `Components/Tree/ClayTreeOptions.cs`;
    -   существующие тесты `ClayTree`.

2.  Не меняй публичный API без необходимости.

3.  Сохрани оба текущих режима получения mutation service:

    -   при заданном `Options.TableName` --- встроенный
        `ClaySqlTreeMutations`;
    -   при отсутствии `TableName` --- `IClayTreeMutations` из DI.

4.  Кэш должен инвалидироваться при изменении любых параметров, влияющих
    на встроенный `ClaySqlTreeMutations`. Минимально проверь:

    -   `ConnectionStringName`;
    -   `TableName`;
    -   `Schema` и конкретные поля schema, которые использует
        `ClaySqlTreeMutations`.

5.  Не полагайся только на reference equality объекта `Options` или
    `Schema`: вызывающий код может переиспользовать тот же экземпляр
    options и изменить его свойства между render cycles.

6.  Не допускай ситуации, когда:

    -   `_dataSource` уже работает с новым подключением;
    -   `_mutationsCached` всё ещё работает со старым
        подключением/таблицей/schema.

7.  Учти DI-path. Если mutation service получен из DI и параметры,
    определяющие способ resolution, изменились, поведение должно
    оставаться предсказуемым. Не уничтожай DI-managed service
    самостоятельно.

8.  Не добавляй blanket `catch (Exception)` и не скрывай ошибки
    конфигурации.

9.  Если для корректного решения нужно хранить snapshot
    mutation-relevant настроек, сделай это явно и компактно.

## Ожидаемое поведение

Пример:

1.  дерево создано с `ConnectionStringName = "DbA"` и
    `TableName = "dbo.TreeA"`;
2.  mutation service впервые использован и закэширован;
3.  родитель передаёт новые options: `ConnectionStringName = "DbB"`
    и/или `TableName = "dbo.TreeB"`;
4.  `OnParametersSetAsync()` обрабатывает изменение;
5.  следующая Edit/Add/Delete/DnD операция обязана использовать новый
    `DbManager` и новый target.

То же требование действует при изменении mutation-relevant schema.

## Тесты

Добавь regression-тесты, насколько позволяет текущая тестовая
архитектура.

Минимально должны быть покрыты сценарии:

-   mutation cache не пересоздаётся при неизменных relevant settings;
-   смена `TableName` приводит к использованию новой конфигурации;
-   смена `ConnectionStringName` не оставляет mutation service
    привязанным к старому `DbManager`;
-   смена relevant schema не оставляет старые имена колонок;
-   переход между встроенным `TableName`-path и DI-path, если такой
    runtime-сценарий поддерживается текущим API.

Если полноценный integration test с `DbManager` затруднителен, выдели
минимальную тестируемую логику snapshot/invalidation, не ухудшая
production design.

## Ограничения

-   Не рефактори весь `ClayTreeView`.
-   Не меняй SQL мутаций, если это не требуется для исправления.
-   Не меняй семантику `ResolveDb()` без отдельной доказанной
    необходимости.
-   Не затрагивай фильтрацию, DnD или reload сверх необходимого.
-   Не выполняй косметический массовый рефакторинг.

## Документация

После исправления кратко актуализируй `Components/Tree/AGENTS.md`, если
lifecycle mutation service является существенным внутренним инвариантом.

## Приёмка

Перед завершением:

1.  запусти релевантные тесты;
2.  запусти полный test suite проекта, если это практически возможно;
3.  проверь сборку;
4.  перечисли изменённые файлы;
5.  кратко объясни, каким образом теперь определяется устаревание
    `_mutationsCached`;
6.  отдельно укажи, какие runtime-сценарии смены options покрыты
    тестами.

Не считай задачу выполненной только потому, что
`_mutationsCached = null` добавлен в один существующий `if`: необходимо
доказать, что проверяются все параметры, от которых реально зависит
созданный mutation service.
