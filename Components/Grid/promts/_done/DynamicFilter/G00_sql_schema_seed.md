> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# G0 — SQL-скрипт: три таблицы + триггер-upsert (п.4) + сид грида #140

Прочитать перед началом: раздел «Configuration — connection string priority» корневого AGENTS.md.

Файл создать: `scripts/dynamic-grid/schema.sql` (SQL Server 2008 R2-совместимый).

Содержание скрипта (имена таблиц/колонок = дефолтам ClayGridSchemaMap из G1):
```sql
-- п.2: настройки грида
CREATE TABLE ClayGridSettings(
  КодЗапроса int NOT NULL PRIMARY KEY,
  Запрос varchar(50) NULL,
  Пиктограмма varchar(50) NULL,
  [SQL] varchar(4000) NULL,
  ID varchar(50) NULL,
  IDName varchar(50) NULL,
  ФормаРедактирования varchar(100) NULL,
  ФормаНового varchar(100) NULL,
  SQLDelete varchar(300) NULL
);
-- п.3: настройки колонок по умолчанию
CREATE TABLE ClayGridColumns(
  КодКолонки int NOT NULL PRIMARY KEY,
  КодЗапроса int NOT NULL,
  Колонка varchar(50) NULL,
  ЗаголовокКолонки varchar(50) NULL,
  КлючURL varchar(50) NULL,
  Порядок int NULL,
  Формат varchar(2000) NULL,
  Тип int NULL
);
-- п.4: параметры пользователя
CREATE TABLE ClayGridUserParams(
  КодНастройкиКлиента int NOT NULL,
  Параметр varchar(20) NOT NULL,
  Значение varchar(1000) NULL,
  CONSTRAINT UQ_ClayGridUserParams UNIQUE(КодНастройкиКлиента, Параметр)
);
```
Триггер-upsert на ClayGridUserParams (приложение шлёт ТОЛЬКО INSERT):
- `CREATE TRIGGER TR_ClayGridUserParams_Upsert ON ClayGridUserParams INSTEAD OF INSERT` —
  для каждой вставляемой строки: если пара (КодНастройкиКлиента, Параметр) уже есть — UPDATE
  Значение, иначе INSERT. Сделать set-based (через MERGE или UPDATE...FROM inserted + INSERT
  недостающих), не курсором.

Сид:
- В ClayGridSettings: (140, 'Медицинские исследования', NULL, 'SELECT КодИсследования, Название,
  ДатаСоздания, КодТипа, Активно FROM Исследования', 'КодИсследования', 'Название',
  '/medical/edit', '/medical/new', 'DELETE FROM Исследования WHERE КодИсследования=@id').
- В ClayGridColumns 5 строк для КодЗапроса=140:
  (1001,140,'КодИсследования','№','id',1,NULL,1),
  (1002,140,'Название','Название','name',2,NULL,2),
  (1003,140,'ДатаСоздания','Создано','created',3,'dd.MM.yyyy',3),
  (1004,140,'КодТипа','Тип исследования','type',4,'SELECT КодТипа, Наименование FROM Типы ORDER BY Наименование',5),
  (1005,140,'Активно','Активно','active',0,'Активно=1',7).

Не делай: не создавай эти таблицы в проде-миграциях; это dev/тест-стенд (в проде таблицы уже
есть и имена другие — их задаёт ClayGridSchemaMap).

Проверка:
- скрипт применяется на LocalDB без ошибок;
- `SELECT * FROM ClayGridSettings WHERE КодЗапроса=140` → 1 строка; `... ClayGridColumns ... =140` → 5 строк;
- `INSERT ClayGridUserParams VALUES(0,'flt140','a')` затем `INSERT ClayGridUserParams VALUES(0,'flt140','b')`
  → в таблице ОДНА строка с Значение='b' (триггер-upsert сработал).
