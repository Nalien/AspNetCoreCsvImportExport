using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using System.Linq;

namespace AspNetCoreCsvImportExport.Formatters
{
    /// <summary>
    /// ContentType: text/csv
    /// </summary>
    public class CsvInputFormatter : InputFormatter
    {
        private readonly CsvFormatterOptions _options;

        public CsvInputFormatter(CsvFormatterOptions csvFormatterOptions)
        {
            this.SupportedMediaTypes.Add(Microsoft.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/csv"));

            if (csvFormatterOptions == null)
            {
                throw new ArgumentNullException(nameof(csvFormatterOptions));
            }

            this._options = csvFormatterOptions;
        }

        public override Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
        {
            var type = context.ModelType;
            var request = context.HttpContext.Request;
            MediaTypeHeaderValue requestContentType = null;
            MediaTypeHeaderValue.TryParse(request.ContentType, out requestContentType);


            var result = this.ReadStream(type, request.Body);
            return InputFormatterResult.SuccessAsync(result);
        }

        public override bool CanRead(InputFormatterContext context)
        {
            var type = context.ModelType;
            if (type == null)
                throw new ArgumentNullException("type");

            return this.IsTypeOfIEnumerable(type);
        }

        private bool IsTypeOfIEnumerable(Type type)
        {

            foreach (Type interfaceType in type.GetInterfaces())
            {

                if (interfaceType == typeof(IList))
                    return true;
            }

            return false;
        }

        private object ReadStream(Type type, Stream stream)
        {
            Type itemType;
            var typeIsArray = false;
            IList list;
            if (type.GetGenericArguments().Length > 0)
            {
                itemType = type.GetGenericArguments()[0];
                list = (IList)Activator.CreateInstance(type);
            }
            else
            {
                typeIsArray = true;
                itemType = type.GetElementType();

                var listType = typeof(List<>);
                var constructedListType = listType.MakeGenericType(itemType);

                list = (IList)Activator.CreateInstance(constructedListType);
            }

            var reader = new StreamReader(stream, Encoding.GetEncoding(this._options.Encoding));

            bool skipFirstLine = this._options.UseSingleLineHeaderInCsv;
            string[] headers = null;
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                var values = line.Split(this._options.CsvDelimiter.ToCharArray());
                if (skipFirstLine)
                {
                    // if skipping first line, assume it contains the headers.
                    skipFirstLine = false;
                    headers = values;
                }
                else
                {
                    var itemTypeInGeneric = list.GetType().GetTypeInfo().GenericTypeArguments[0];
                    var item = Activator.CreateInstance(itemTypeInGeneric);
                    var properties = this._options.UseNewtonsoftJsonDataAnnotations
                        ? item.GetType().GetProperties().Where(pi => !pi.GetCustomAttributes<JsonIgnoreAttribute>().Any()).ToArray()
                        : item.GetType().GetProperties();

                    // TODO: Maybe refactor to not use positional mapping?, mapping by index could generate errors pretty easily :)
                    for (int i = 0; i < values.Length; i++)
                    {
                        // we have headers, so use them
                        if (headers != null)
                        {
                            var property = properties.SingleOrDefault(x => x.Name == headers[i] || x.GetCustomAttributes<JsonPropertyAttribute>()?.FirstOrDefault()?.PropertyName == headers[i]);
                            property?.SetValue(item, Convert.ChangeType(values[i], property.PropertyType), null);
                        }
                        // else use property index.
                        else
                        {
                            properties[i].SetValue(item, Convert.ChangeType(values[i], properties[i].PropertyType), null);
                        }
                    }

                    list.Add(item);
                }

            }

            if (typeIsArray)
            {
                Array array = Array.CreateInstance(itemType, list.Count);

                for (int t = 0; t < list.Count; t++)
                {
                    array.SetValue(list[t], t);
                }
                return array;
            }

            return list;
        }
    }
}
