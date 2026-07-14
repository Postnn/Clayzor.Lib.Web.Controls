> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# TG5 — параметры пользователя: имена/CLID (к G6/G7)  → UserParamsTests.cs
```
- ClayGridUserParamsData.BuildParamName("flt",140)=="flt140"; ("cols",1)=="cols1";
- CLID: если query-параметра CLID нет → используется 0 (проверить на хелпере извлечения CLID);
- BuildInsertSql содержит "INSERT INTO" и НЕ содержит "UPDATE"/"MERGE" (upsert делает триггер БД);
- BuildLoadSql на 2 имени содержит "IN (@n0,@n1)" и параметр @clid.
```
