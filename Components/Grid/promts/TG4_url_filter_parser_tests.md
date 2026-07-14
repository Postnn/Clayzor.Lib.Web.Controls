> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# TG4 — разбор URL-фильтра, правила 1,2,5 (к G8) — КЛЮЧЕВОЙ  → UrlFilterParserTests.cs
```
[Theory] ClayGridUrlFilterParser.Parse:
- ("name","eq~DQA1",col:Text) → Operator=Equals, Value="DQA1", IsForced=true, IsDefault=false;
- ("created","ge~20260101",col:Date) → GreaterOrEqual,"20260101";
- ("type","in~3,5",col:Number) → In,"3,5";
- ("created","between~20260101~20260401",Date) → Between с обеими границами;
- ("created","20260101",Date) без "op~" → Operator = дефолт Date (Equals), Value="20260101" (правило 5);
- ("_name","eq~x",Text) → IsDefault=true (ключ 'name');
[Fact] Apply-логика:
- _key при отсутствии savedUserParams[key] → условие добавлено; при наличии → НЕ добавлено (сохранённое победило);
- key без '_' → добавлено и помечено forced (не для сохранения).
```
