using Avalonia.Markup.Xaml;

namespace TableLens.Desktop;

// Deliberately small: two strings per key, no localization framework to maintain.
public static class Lang
{
    public static string Language { get; set; } = "uk";
    public static string T(string key) => Strings.TryGetValue(key, out var pair) ? Language == "en" ? pair.En : pair.Uk : key;
    private static readonly Dictionary<string, (string Uk, string En)> Strings = new()
    {
        ["File"] = ("Файл", "File"), ["Project"] = ("Проєкт", "Project"), ["Edit"] = ("Редагування", "Edit"), ["Tools"] = ("Інструменти", "Tools"), ["Help"] = ("Довідка", "Help"),
        ["Open"] = ("Відкрити файли", "Open files"), ["Folder"] = ("Відкрити папку", "Open folder"), ["Add"] = ("Додати файли", "Add files"), ["New"] = ("Нова таблиця", "New table"),
        ["Save"] = ("Зберегти", "Save"), ["Export"] = ("Експорт", "Export"), ["Exit"] = ("Вийти", "Quit"), ["Settings"] = ("Налаштування", "Settings"),
        ["NewProject"] = ("Новий проєкт", "New project"), ["OpenProject"] = ("Відкрити проєкт", "Open project"), ["SaveProject"] = ("Зберегти проєкт", "Save project"), ["CloseProject"] = ("Закрити проєкт", "Close project"),
        ["Undo"] = ("Скасувати", "Undo"), ["Redo"] = ("Повторити", "Redo"), ["History"] = ("Історія змін", "Edit history"), ["SchemaEdit"] = ("Змінити структуру DBF", "Edit DBF schema"),
        ["Compare"] = ("Порівняти структури файлів", "Compare file schemas"), ["CompareFolders"] = ("Порівняти папки DBF", "Compare DBF folders"), ["Catalog"] = ("Довідник DBF-полів", "DBF field catalog"), ["About"] = ("Про TableLens", "About TableLens"), ["Developer"] = ("Розробник", "Developer"),
        ["Workspace"] = ("РОБОЧА ОБЛАСТЬ", "WORKSPACE"), ["LooseFiles"] = ("Файли без проєкту", "Loose files"), ["Files"] = ("ФАЙЛИ", "FILES"), ["FileSearch"] = ("Знайти файл…", "Find a file…"),
        ["Remove"] = ("Прибрати зі списку", "Remove from list"), ["Clear"] = ("Очистити список", "Clear list"), ["Reload"] = ("Перечитати", "Reload"),
        ["Subtitle"] = ("Локальні дані. Зрозумілий огляд.", "Local data. A clearer view."), ["Search"] = ("Пошук у всіх полях…", "Search all fields…"), ["SearchHelp"] = ("Пошук без урахування регістру. Фільтри колонок застосовуються разом із пошуком.", "Case-insensitive search. Column filters are combined with search."),
        ["ReadOnly"] = ("Перегляд", "Read only"), ["Editing"] = ("Редагування", "Editing"), ["EnableEdit"] = ("Редагувати", "Enable editing"), ["DisableEdit"] = ("Завершити редагування", "Finish editing"),
        ["AddRow"] = ("Додати рядок", "Add row"), ["EditRow"] = ("Редагувати рядок", "Edit row"), ["DeleteRows"] = ("Видалити рядки", "Delete rows"), ["Data"] = ("Дані", "Data"), ["Schema"] = ("Структура", "Schema"), ["Info"] = ("Інформація", "Information"),
        ["FilterHint"] = ("Фільтр поля: текст, ==значення, !=, >10, або ==A or ==B", "Column filter: text, ==value, !=, >10, or ==A or ==B"), ["Column"] = ("Поле", "Column"), ["Condition"] = ("Умова", "Condition"), ["Apply"] = ("Застосувати", "Apply"), ["Reset"] = ("Скинути", "Reset"),
        ["Welcome"] = ("Ваші таблиці — в одному місці", "Your tables, in one place"), ["WelcomeText"] = ("Перетягніть файли чи папку або відкрийте їх кнопкою нижче.\nDBF, CSV, TSV та JSON — без сервера й завантаження даних у мережу.", "Drop files or a folder, or open them below.\nDBF, CSV, TSV and JSON — no server, no data uploads."),
        ["Samples"] = ("Відкрити приклади", "Try sample files"), ["Recent"] = ("Нещодавні файли", "Recent files"), ["Ready"] = ("Готово", "Ready"), ["Loading"] = ("Читання файлу…", "Reading file…"), ["Cancel"] = ("Скасувати", "Cancel"), ["Close"] = ("Закрити", "Close"),
        ["Error"] = ("Не вдалося виконати дію", "Action could not be completed"), ["Unsaved"] = ("Незбережені зміни", "Unsaved changes"), ["UnsavedText"] = ("У таблиці є незбережені зміни. Зберегти їх перед продовженням?", "This table has unsaved changes. Save before continuing?"), ["Discard"] = ("Відкинути зміни", "Discard changes"),
        ["UnsavedProject"] = ("Зміни проєкту", "Project changes"), ["UnsavedProjectText"] = ("Список файлів або параметри проєкту змінилися. Зберегти проєкт?", "The project file list or import options changed. Save the project?"),
        ["ExportTitle"] = ("Експортувати таблицю", "Export table"), ["Format"] = ("Формат", "Format"), ["ExportVisible"] = ("Лише відфільтровані рядки", "Only filtered rows"), ["ExportAll"] = ("Усі рядки", "All rows"),
        ["ExportHelp"] = ("CSV експортується в UTF-8. JSON зберігає типи значень. Оригінал залишається на місці.", "CSV exports use UTF-8. JSON preserves value types. Your source file stays in place."),
        ["Import"] = ("Параметри імпорту", "Import options"), ["Encoding"] = ("Кодування", "Encoding"), ["Delimiter"] = ("Роздільник CSV", "CSV delimiter"), ["Header"] = ("Перший рядок CSV — заголовки", "First CSV row contains headers"), ["Auto"] = ("Автоматично", "Automatic"),
        ["JsonProperty"] = ("Поле JSON з масивом (необов'язково)", "JSON array property (optional)"), ["Theme"] = ("Тема", "Theme"), ["Language"] = ("Мова інтерфейсу", "Interface language"), ["System"] = ("Системна", "System"), ["Light"] = ("Світла", "Light"), ["Dark"] = ("Темна", "Dark"),
        ["RestartLanguage"] = ("Нова мова застосовується після перезапуску.", "Language changes apply after restarting."), ["ImportHelp"] = ("Параметри застосовуються до вибраного файлу після перечитування, а також до нових файлів.", "Options apply to the selected file after reloading and to newly opened files."),
        ["Name"] = ("Назва", "Name"), ["Type"] = ("Тип", "Type"), ["Length"] = ("Довжина", "Length"), ["Decimals"] = ("Десяткові", "Decimals"), ["Description"] = ("Опис", "Description"), ["Status"] = ("Стан", "Status"),
        ["NewTableName"] = ("Оберіть формат і задайте назви полів. DBF-структуру можна налаштувати на наступному кроці.", "Choose a format and column names. You can configure DBF fields in the next step."), ["Columns"] = ("Назви полів, через кому", "Column names, comma-separated"),
        ["SchemaHelp"] = ("Зміни структури діють у пам'яті до збереження. Для перейменування залиште початкове поле у колонці «Джерело».", "Schema edits stay in memory until saved. Keep the original field in Source when renaming."), ["Source"] = ("Джерело", "Source"), ["AddField"] = ("Додати поле", "Add field"), ["DeleteField"] = ("Прибрати поле", "Remove field"),
        ["DeleteConfirm"] = ("Видалити вибрані рядки? Цю дію можна скасувати.", "Delete selected rows? You can undo this action."), ["Delete"] = ("Видалити", "Delete"), ["SelectRow"] = ("Спочатку виберіть рядок у таблиці.", "Select a row in the table first."),
        ["Null"] = ("NULL / відсутнє значення", "NULL / missing value"), ["RowHelp"] = ("Порожній текст і NULL — різні значення. Вкладені JSON-поля вводьте як коректний JSON.", "Empty text and NULL are different. Enter nested JSON fields as valid JSON."),
        ["Path"] = ("Шлях", "Path"), ["Rows"] = ("Рядків", "Rows"), ["FieldsCount"] = ("Полів", "Columns"), ["Size"] = ("Розмір", "Size"), ["Modified"] = ("Змінено", "Modified"), ["Backup"] = ("Резервна копія", "Backup"),
        ["CatalogHelp"] = ("Оновіть описи полів або замініть еталон поточною структурою. Перед зміною довідника створюється .bak.", "Edit field descriptions or replace the reference with this schema. Catalog changes create a .bak backup."), ["UpdateDescriptions"] = ("Оновити описи", "Update descriptions"), ["ReplaceReference"] = ("Замінити еталон", "Replace reference"),
        ["CatalogUnavailable"] = ("Довідник не завантажено. Файли можна переглядати; перевірте конфігурацію у папці налаштувань.", "The catalog could not be loaded. File viewing is available; check the configuration in the settings folder."),
        ["Saved"] = ("Збережено", "Saved"), ["Exported"] = ("Експорт завершено", "Export complete"), ["Missing"] = ("Файл не знайдено", "File missing"), ["FreeMode"] = ("Додавайте файли без створення проєкту", "Add files without creating a project"),
        ["FilterInvalid"] = ("Некоректний фільтр", "Invalid filter"), ["FilterCount"] = ("Активні фільтри", "Active filters"), ["Working"] = ("Виконується…", "Working…"), ["NoChanges"] = ("Змін ще немає", "No changes yet"),
        ["HistoryHelp"] = ("Історія активної сесії. Після збереження починається нова сесія.", "History for the active session. Saving starts a new session."), ["Applied"] = ("Застосовано", "Applied"), ["Undone"] = ("Скасовано", "Undone"),
        ["ComparePickLeft"] = ("Оберіть лівий файл", "Choose the left file"), ["ComparePickRight"] = ("Оберіть правий файл", "Choose the right file"), ["CompareFolderLeft"] = ("Оберіть ліву папку", "Choose the left folder"), ["CompareFolderRight"] = ("Оберіть праву папку", "Choose the right folder"),
        ["ReplaceConfirm"] = ("Замінити еталон у довіднику поточною структурою?", "Replace the catalog reference with the current schema?"), ["RemoveMissing"] = ("Прибрати відсутні поля з еталона", "Remove missing reference fields"),
        ["Shortcuts"] = ("Клавіатурні скорочення", "Keyboard shortcuts"), ["FilesEmpty"] = ("Відкрийте файл, щоб почати", "Open a file to get started"), ["Confirm"] = ("Підтвердити", "Confirm"),
        ["NoReference"] = ("Без еталона", "No reference schema"),
        ["CopyCell"] = ("Копіювати клітинку", "Copy cell"), ["CopyRows"] = ("Копіювати вибрані рядки", "Copy selected rows"),
    };
}

public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => Lang.T(Key);
}
