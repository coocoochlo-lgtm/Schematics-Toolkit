using System;
using System.IO;
using System.Reflection;

namespace REDox.Benchmarks;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class DataSourceAttribute : Attribute
{
    private readonly string _filePath;

    public DataSourceAttribute(string filePath)
    {
        _filePath = filePath;
    }

    public static string GetFullPath<T>()
    {
        var attr = typeof(T).GetCustomAttribute<DataSourceAttribute>();

        if (attr is null)
        {
            throw new ArgumentNullException(nameof(attr));
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var path = Path.Combine(
                current.FullName,
                attr._filePath);

            if (File.Exists(path))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"File not found {attr._filePath}");
    }
}