# CTFR3 --- Явный контракт неуспеха DML при connectivity error

## Контекст

Репозитории/проекты необходимо определить по solution. Основные
затронутые области:

-   `Clayzor.Lib.DALC` / `DbManager`;
-   `Clayzor.Lib.Web.Controls`;
-   `Components/Tree/ClaySqlTreeMutations.cs`;
-   `Components/Tree/ClayTreeView.Mutations.cs`;
-   `Components/Tree/ClayTreeDragDrop.cs`.

Финальное ревью `ClayTree` обнаружило архитектурный стык.

`ClaySqlTreeMutations` выполняет DML через `DbManager.ExecuteAsync(...)`
и после `await` считает операцию успешной.

При этом текущий DALC-path для connectivity `SqlException` может:

1.  передать ошибку в `ISqlErrorHandler`;
2.  не пробросить exception наружу;
3.  вернуть `default` вызывающему коду.

В результате UPDATE/INSERT/DELETE может фактически не выполниться, но
`ClaySqlTreeMutations` завершит Task как успешный, а `ClayTreeView`
продолжит success-path:

-   reload;
-   изменение selection;
-   `RestoreFocus`;
-   локальное изменение `HasChildren`/expanded state.

Это создаёт ложный success state.

## Цель

Сформировать и реализовать однозначный контракт: вызывающий DML-код
должен уметь отличить успешное выполнение SQL от connectivity failure,
даже если UI-показ ошибки централизован через `ISqlErrorHandler`.

Исправление должно быть системным и не должно дублировать показ одной
SQL-ошибки на нескольких уровнях.

## Сначала проведи аудит

Перед изменениями найди все вызовы и контракты:

-   `DbManager.RunAsync`;
-   `ExecuteAsync`;
-   `ExecuteScalarAsync`;
-   query/load methods;
-   `ISqlErrorHandler`;
-   обработку connectivity vs ordinary SQL errors;
-   все consumers `ExecuteAsync`, особенно mutation/write services.

Определи, почему DALC сейчас возвращает `default` и какие компоненты
зависят от такого поведения.

Не начинай с механического `throw;` в одном месте без оценки регрессий.

## Требования к решению

1.  После connectivity failure write-caller не должен получить сигнал
    «успешно».

2.  `ClayTree` должен выполнять post-mutation reload/state updates
    только если DML действительно завершился успешно.

3.  Ошибка не должна показываться пользователю дважды.

4.  Сохрани централизованный `ISqlErrorHandler` там, где он является
    текущим проектным решением.

5.  Обычные non-connectivity `SqlException` должны сохранять ожидаемую
    семантику проекта. Не смешивай без анализа:

    -   validation/constraint SQL errors;
    -   connectivity errors;
    -   cancellation;
    -   programming/configuration errors.

6.  `OperationCanceledException` не превращай в SQL failure result.

7.  Не используй `catch (Exception)` для маскировки проблемы.

8.  Предпочтителен общий DALC-контракт, а не ClayTree-specific
    workaround, если проблема действительно распространяется на все DML
    consumers.

## Возможные направления

Не считай этот список предписанием --- выбери решение после аудита:

-   connectivity exception после вызова handler всё равно
    пробрасывается, а верхний UI-level знает, что она уже обработана;
-   typed result (`Success/Failure`) для write operations;
-   отдельный write API;
-   иной явный сигнал failure, не совместимый с ложным success.

Если меняется публичный DALC API, минимизируй breaking changes и обоснуй
их.

Особенно осторожно относись к идее трактовать `ExecuteAsync == 0` как
connectivity failure: для корректного SQL zero affected rows может быть
валидным результатом. Нельзя смешивать эти состояния.

## ClayTree

После определения DALC-контракта адаптируй
`ClaySqlTreeMutations`/`ClayTreeView`, если это требуется.

Проверь все mutation flows:

-   `UpdateNodeAsync`;
-   `AddChildAsync`;
-   `DeleteAsync`;
-   `ReparentAsync`;
-   `ReorderAsync`;
-   `GetNodePathAsync`;
-   `IsDescendantAsync`.

Для write operations failure должен прерывать success-path.

`GetNodePathAsync` остаётся optional operation в UI: текущая логика
может сознательно возвращать `null`/не блокировать диалог, но не меняй
её случайно вместе с DML semantics.

## Тесты

Добавь regression coverage минимум для:

-   успешный DML;
-   connectivity failure;
-   ordinary SQL failure;
-   cancellation;
-   корректный DML с `0 affected rows`, если такой результат допустим
    API;
-   отсутствие post-success действий `ClayTree` после connectivity
    failure;
-   отсутствие двойного вызова/двойного UI-report одной ошибки.

Если DALC используется другими write-компонентами, добавь тест на общий
контракт, а не только на Tree.

## Документация

Обнови соответствующие `AGENTS.md`/docs:

-   кто отвечает за показ SQL error;
-   что возвращает/бросает DALC при connectivity failure;
-   как write-caller определяет success;
-   где заканчивается ответственность `ISqlErrorHandler`.

## Ограничения

-   Не делай ClayTree-only костыль, если корень проблемы в DALC.
-   Не меняй read/query semantics без необходимости.
-   Не превращай все ошибки в `default`.
-   Не делай двойной toast/dialog.
-   Не ломай cancellation semantics.
-   Не выполняй несвязанный рефакторинг DALC.

## Приёмка

В финальном отчёте обязательно:

1.  опиши прежний failure flow;
2.  опиши новый контракт;
3.  перечисли затронутые consumers;
4.  перечисли изменённые файлы;
5.  покажи tests для connectivity/non-connectivity/cancellation;
6.  подтверди, что ClayTree больше не выполняет reload/state
    success-path после фактически неисполненного DML;
7.  укажи все потенциальные breaking changes API.
