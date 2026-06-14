namespace TableLens.Core.Dbf;

public static class DbfFileAccess
{
    public static void EnsureAvailable(string filePath)
    {
        if (!TryCheckAvailable(filePath, out var problem)) throw new IOException(problem);
    }

    public static bool TryCheckAvailable(string filePath, out string problem)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1,
                FileOptions.RandomAccess);
            problem = string.Empty;
            return true;
        }
        catch (IOException)
        {
            problem = "Файл використовується іншою програмою. Закрийте його, наприклад у DBFNavigator, і повторіть спробу.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            problem = "Немає доступу до файлу. Перевірте права доступу або закрийте програму, яка його використовує.";
            return false;
        }
    }
}
