using System;
using System.Data;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Data.SqlClient;
using System.Collections.Generic;

namespace EasyAccess
{
    #region EasyAccess

    #region Attributes

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class BaseAttribute : Attribute { }

    public class ColumnAttribute : BaseAttribute
    {
        public ColumnAttribute(SqlDbType sqlDbType, string name = default) =>
            (SqlDbType, Name) = (sqlDbType, name);

        public string Name { get; set; }
        public SqlDbType SqlDbType { get; set; }
    }

    public class IdColumnAttribute : ColumnAttribute
    {
        public IdColumnAttribute() : base(SqlDbType.Int, "Id") { }
    }

    #endregion

    #region Data Helpers

    public class DataList<TData> : List<TData>, IList<TData>
    {
        public static DataList<TData> Empty =>
            new DataList<TData>();
    }

    #endregion

    #region Extensions

    public static class DataReaderExtensions
    {
        public static T GetValueOrDefault<T>(this IDataRecord dataRecord, string columnName) =>
            dataRecord.GetValueOrDefault<T>(dataRecord.GetOrdinal(columnName));

        public static T GetValueOrDefault<T>(this IDataRecord dataRecord, int columnIndex) =>
            dataRecord.IsDBNull(columnIndex) ? default : (T)dataRecord.GetValue(columnIndex);
    }

    internal static class DbConnectionExtensions
    {
        public static IDbConnection EnsureOpen(this IDbConnection connection)
        {
            if (connection.State == ConnectionState.Closed || connection.State == ConnectionState.Broken)
            {
                connection.Open();
            }

            return connection;
        }
    }

    #endregion

    public sealed class EasyAccess
    {
        private readonly string _connectionString;

        public EasyAccess(string connectionString) =>
            _connectionString = connectionString;

        public static EasyAccess Create(string connectionString) =>
            new EasyAccess(connectionString);

        #region Helpers

        private IEnumerable<(string columnName, object value, SqlDbType sqlDbType)> GetColumnNamesValuesAndTypesWithoutId<TData>(TData data) => data
            .GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(ColumnAttribute)) && !p.IsDefined(typeof(IdColumnAttribute)))
            .Select(p =>
            {
                var columnAttribute = p.GetCustomAttribute<ColumnAttribute>(true)
                    ?? throw new Exception($"Property should be marked with '{nameof(ColumnAttribute)}'");

                var columnName = columnAttribute.Name;

                var value = p.IsDefined(typeof(TypeColumnAttribute), true) ?
                    ConvertorHelper.GetPrimitiveFromCustom(p, p.GetValue(data)) : p.GetValue(data);

                return (string.IsNullOrWhiteSpace(columnName) ? p.Name : columnName, value, columnAttribute.SqlDbType);
            });

        #endregion

        public TResult QuerySingle<TResult>(string table, string condition, Func<IDataReader, TResult> map)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = connection.CreateCommand();
            connection.EnsureOpen();

            command.CommandText = $"SELECT * FROM {table} WHERE {condition}";

            var reader = command.ExecuteReader();

            return reader.Read() ? map(reader) : default;
        }

        public DataList<TResult> Query<TResult>(string table, string condition, Func<IDataReader, TResult> map)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = connection.CreateCommand();
            connection.EnsureOpen();

            command.CommandText = new StringBuilder()
                .Append($"SELECT * FROM {table} ")
                .Append(!string.IsNullOrWhiteSpace(condition) ? $"WHERE {condition}" : string.Empty)
                .ToString();

            var reader = command.ExecuteReader();

            var dataList = DataList<TResult>.Empty;

            while (reader.Read())
            {
                dataList.Add(map(reader));
            }

            return dataList;
        }

        public DataList<TResult> Query<TResult>(string table, Func<IDataReader, TResult> map) =>
            Query(table, default, map);

        public (int id, bool inserted) Insert<TData>(string table, TData data)
        {
            #region Prepare Parameters and Query

            string BuildInsertQuery(string table, IEnumerable<string> columnNames, bool withId = true) => new StringBuilder()
                .Append($"INSERT INTO dbo.{table}")
                .Append($"({string.Join(",", columnNames)}) ")
                .Append($"VALUES({string.Join(",", columnNames.Select(c => $"@{c}"))})")
                .Append(withId ? " SELECT @Id = @@IDENTITY" : string.Empty)
                .ToString();

            (string query, List<SqlParameter> parameters) GetInsertQueryAndParameters(string table, TData data)
            {
                var columnNamesValuesAndTypes = GetColumnNamesValuesAndTypesWithoutId(data);

                var query = BuildInsertQuery(table,
                    columnNamesValuesAndTypes.Select(t => t.columnName));

                var sqlParameters = columnNamesValuesAndTypes
                    .Aggregate(new List<SqlParameter>(), (acc, item) =>
                    {
                        acc.Add(new SqlParameter($"@{item.columnName}", item.value ?? DBNull.Value) { SqlDbType = item.sqlDbType });
                        return acc;
                    });

                return (query, sqlParameters);
            }

            #endregion

            using var connection = new SqlConnection(_connectionString);
            using var command = connection.CreateCommand();
            connection.EnsureOpen();

            var (query, parameters) = GetInsertQueryAndParameters(table, data);
            command.CommandText = query;

            var idParameter = new SqlParameter("@Id", SqlDbType.Int) { Direction = ParameterDirection.Output };

            command.Parameters.Add(idParameter);

            parameters.ForEach(p => command.Parameters.Add(p));

            var rowsAffected = command.ExecuteNonQuery();

            return ((int)idParameter.Value, rowsAffected > 0);
        }

        public (int id, bool updated) Update<TData>(string table, TData data)
        {
            #region Prepare Parameters and Query / Helpers

            string BuildUpdateQuery(string table, IEnumerable<string> columnNames) => new StringBuilder()
                .Append($"UPDATE dbo.{table} SET ")
                .Append($"{string.Join(",", columnNames.Select(c => $"{c} = @{c}"))} ")
                .Append("WHERE Id = @Id")
                .ToString();

            (string query, List<SqlParameter> parameters) GetUpdateQueryAndParameters(string table, TData data)
            {
                var columnNamesValuesAndTypes = GetColumnNamesValuesAndTypesWithoutId(data);

                var query = BuildUpdateQuery(table,
                    columnNamesValuesAndTypes.Select(t => t.columnName));

                var sqlParameters = new List<SqlParameter>() { new SqlParameter("@Id", GetId(data)) };

                sqlParameters = columnNamesValuesAndTypes
                    .Aggregate(sqlParameters, (acc, item) =>
                    {
                        acc.Add(new SqlParameter($"@{item.columnName}", item.value ?? DBNull.Value) { SqlDbType = item.sqlDbType });
                        return acc;
                    });

                return (query, sqlParameters);
            }

            object GetId(TData data) => data
                .GetType()
                .GetProperties()
                .FirstOrDefault(p => p.GetCustomAttribute<IdColumnAttribute>() is { })
                ?.GetValue(data);

            #endregion

            using var connection = new SqlConnection(_connectionString);
            using var command = connection.CreateCommand();
            connection.EnsureOpen();

            var (query, parameters) = GetUpdateQueryAndParameters(table, data);

            command.CommandText = query;

            parameters.ForEach(p => command.Parameters.Add(p));

            var rowsAffected = command.ExecuteNonQuery();

            return ((int)GetId(data), rowsAffected > 0);
        }

        public bool Delete(string table, int id)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = connection.CreateCommand();
            connection.EnsureOpen();

            command.Parameters.Add(new SqlParameter("@Id", id));
            command.CommandText = $"DELETE FROM dbo.{table} WHERE Id = @Id";

            return command.ExecuteNonQuery() > 0;
        }
    }

    #endregion

    #region EasyAccess.CustomTypes

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Struct, AllowMultiple = false)]
    public class TypeColumnAttribute : ColumnAttribute
    {
        public TypeColumnAttribute(SqlDbType sqlDbType, string name = null) : base(sqlDbType, name) { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public class ConvertorAttribute : Attribute
    {
        public ConvertorAttribute(Type of) =>
            Of = of;

        public Type Of { get; set; }
    }

    public interface IConvertor<TCustom, TPrimitive>
    {
        TCustom CustomFromPrimitive(TPrimitive primitive);
        TPrimitive PrimitiveFromCustom(TCustom custom);
    }

    public static class ConvertorHelper
    {
        private const string CustomFromPrimitiveMethod = "CustomFromPrimitive";
        private const string PrimitiveFromCustomMethod = "PrimitiveFromCustom";

        private static (object convertor, MethodInfo method) GetConvertorAndMethod(PropertyInfo property, string method)
        {
            var convertorType = Assembly
                .GetExecutingAssembly()
                .GetTypes()
                .FirstOrDefault(t =>
                {
                    var convertorAttribute = t.GetCustomAttribute<ConvertorAttribute>();

                    return convertorAttribute is { } && convertorAttribute.Of.Equals(property.PropertyType);
                });

            if (convertorType is null)
            {
                throw new Exception(
                    $"Custom type <{property.PropertyType.Name}> needs Convertor class that implements IConvertor interface, \n" +
                    $"Convertor should be marked with <{nameof(ConvertorAttribute)}>");
            }

            try
            {
                return (Activator.CreateInstance(convertorType), convertorType.GetMethod(method));
            }
            catch (Exception ex)
            {
                throw new Exception($"Something went wrong in Convertor class '{convertorType.Name}'", ex);
            }
        }

        public static object GetCustomFromPrimitive(PropertyInfo property, object primitive)
        {
            var (convertor, method) = GetConvertorAndMethod(property, CustomFromPrimitiveMethod);

            try
            {
                return method?.Invoke(convertor, new[] { primitive });
            }
            catch (Exception ex)
            {
                throw new Exception($"Something went wrong when converting primitive type to '{property.PropertyType.Name}'", ex);
            }
        }

        public static object GetPrimitiveFromCustom(PropertyInfo property, object custom)
        {
            var (convertor, method) = GetConvertorAndMethod(property, PrimitiveFromCustomMethod);

            try
            {
                return method?.Invoke(convertor, new[] { custom });
            }
            catch (Exception ex)
            {
                throw new Exception($"Something went wrong when converting '{property.PropertyType.Name}' to primitive type", ex);
            }
        }
    }

    #endregion

    #region EasyAccess.Mapper

    public static class DataReaderExtensionsForMapper
    {
        public static object GetValueOrDefault(this IDataRecord dataRecord, string columnName) =>
            dataRecord.GetValueOrDefault(dataRecord.GetOrdinal(columnName));

        public static object GetValueOrDefault(this IDataRecord dataRecord, int columnIndex) =>
            dataRecord.IsDBNull(columnIndex) ? default : dataRecord.GetValue(columnIndex);
    }

    public static class Mapper
    {
        public static TResult MapperOf<TResult>(IDataReader dataReader) where TResult : new()
        {
            var resultType = typeof(TResult);
            var result = Activator.CreateInstance(resultType); // with constructor ?

            resultType
                .GetProperties()
                .Where(p => p.GetCustomAttribute<ColumnAttribute>() is { })
                .Select(property =>
                {
                    var columnName = property.GetCustomAttribute<ColumnAttribute>(true)?.Name;

                    return (property, columnName: string.IsNullOrWhiteSpace(columnName) ? property.Name : columnName);
                })
                .ToList()
                .ForEach(propertyAndColumnName =>
                {
                    var (property, columnName) = propertyAndColumnName;

                    var dbValue = dataReader.GetValueOrDefault(columnName);

                    if (property.GetCustomAttribute<TypeColumnAttribute>() is { })
                    {
                        property.SetValue(result, ConvertorHelper.GetCustomFromPrimitive(property, dbValue));
                    }
                    else
                    {
                        property.SetValue(result, dbValue);
                    }
                });

            return (TResult)result;
        }
    }

    #endregion

    #region EasyAccess.ConditionBuilder

    public static class ConditionBuilder
    {
        private static (string columnName, object value) GetColumnNameAndValue<TModel>(string propertyName, object value)
        {
            var property = typeof(TModel)
                .GetProperties()
                .FirstOrDefault(p => p.Name.Equals(propertyName));

            var columnAttribute = property.GetCustomAttribute<ColumnAttribute>(true)
                ?? throw new Exception($"Property should be marked with '{nameof(ColumnAttribute)}'");

            return (string.IsNullOrWhiteSpace(columnAttribute.Name) ? propertyName : columnAttribute.Name,
                columnAttribute is TypeColumnAttribute ? ConvertorHelper.GetPrimitiveFromCustom(property, value) : value);
        }

        private static string InsertCondition<TModel>(this string query, string propertyName, object value, string condition)
        {
            var (columnName, finalValue) = GetColumnNameAndValue<TModel>(propertyName, value);

            return $"{query}{columnName} {condition} {IfStringUseApostroph(finalValue)} ";

            string IfStringUseApostroph(object value) =>
                value is string ? $"'{value}'" : value.ToString();
        }

        public static string And(this string query) => $"{query}AND ";
        public static string Or(this string query) => $"{query}OR ";
        public static string Not(this string query) => $"{query}NOT ";

        public static string ColumnEquals<TModel>(string propertyName, object value) =>
            ColumnEquals<TModel>(string.Empty, propertyName, value);
        public static string ColumnEquals<TModel>(this string query, string propertyName, object value) =>
            query.InsertCondition<TModel>(propertyName, value, "=");

        public static string ColumnGreater<TModel>(string propertyName, object value) =>
            ColumnGreater<TModel>(string.Empty, propertyName, value);
        public static string ColumnGreater<TModel>(this string query, string propertyName, object value) =>
            query.InsertCondition<TModel>(propertyName, value, ">");
    }

    #endregion

    #region EasyAccess.Creator

    public static class Creator
    {
        private static bool IsNullable(Type type) =>
            Nullable.GetUnderlyingType(type) != null;

        private static IEnumerable<(string columnName, bool isNullable, SqlDbType sqlDbType)> GetDatabaseModelColumns<TModel>() => typeof(TModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(ColumnAttribute)))
            .Select(p =>
            {
                var columnAttribute = p.GetCustomAttribute<ColumnAttribute>(true)
                    ?? throw new Exception($"Property should be marked with '{nameof(ColumnAttribute)}'");

                var columnName = columnAttribute.Name;

                var isNullable = IsNullable(p.PropertyType) || p.PropertyType.IsClass;

                return (string.IsNullOrWhiteSpace(columnName) ? p.Name : columnName, isNullable, columnAttribute.SqlDbType);
            });

        public static void CreateTable<TModel>(string connection, string tableName = default)
        {
            static string GetTableName(string tableName) => string.IsNullOrWhiteSpace(tableName)
                ? $"{typeof(TModel).Name}s"
                : tableName;

            static string GetSqlDbTypeSuffix(SqlDbType sqlDbType)
            {
                switch (sqlDbType)
                {
                    case SqlDbType.Binary:
                    case SqlDbType.Char:
                    case SqlDbType.NVarChar:
                    case SqlDbType.VarBinary:
                    case SqlDbType.VarChar:
                        return " (MAX) ";
                    default: return string.Empty;
                };
            }

            static string GetCreateCommand(string tableName) => new StringBuilder()
                .Append($"CREATE TABLE {tableName}") // [IF NOT EXISTS]
                .Append(" ( " + string.Join(",", GetDatabaseModelColumns<TModel>().Select(m =>
                {
                    return $"{m.columnName} {(m.sqlDbType)}{GetSqlDbTypeSuffix(m.sqlDbType)} " +
                    $"{m switch { ("Id", _, _) => "IDENTITY (1, 1) NOT NULL", (_, true, _) => "NULL", (_, false, _) => "NOT NULL" }}";
                })) + $", CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED ([Id] ASC));")
                .ToString();

            using var command = new SqlConnection(connection).EnsureOpen().CreateCommand();

            command.CommandText = GetCreateCommand(
                GetTableName(tableName));

            command.ExecuteNonQuery();
        }
    }

    #endregion
}