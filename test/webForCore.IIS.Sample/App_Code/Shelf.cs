using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;

namespace IISSample
{
    // Data binding over the application's own types (data/Binding.aspx): TypeDescriptor, a TypeConverter, [MetadataType]
    // buddy classes, an ObjectDataSource and a DataTable column of an application type.
    [TypeConverter(typeof(SizeCodeConverter))]
    public struct SizeCode
    {
        public string Code;
        public override string ToString() => "size:" + Code;
    }

    public class SizeCodeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) => sourceType == typeof(string);
        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) => new SizeCode { Code = (string)value };
    }

    [MetadataType(typeof(BookMetadata))]
    public class Book
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public SizeCode Size { get; set; }
    }

    public class BookMetadata
    {
        [Display(Name = "Book title")]
        [Required]
        public object Title { get; set; }
    }

    [DataObject]
    public class Shelf
    {
        [DataObjectMethod(DataObjectMethodType.Select)]
        public List<Book> GetBooks() => new List<Book>
        {
            new Book { Id = 1, Title = "Dune", Size = (SizeCode)TypeDescriptor.GetConverter(typeof(SizeCode)).ConvertFrom("L") },
            new Book { Id = 2, Title = "Emma", Size = new SizeCode { Code = "M" } },
        };

        public static DataTable Table()
        {
            var table = new DataTable("Books");
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Book", typeof(Book));
            table.Columns.Add("Size", typeof(SizeCode));
            foreach (var book in new Shelf().GetBooks()) table.Rows.Add(book.Id, book, book.Size);
            return table;
        }
    }
}
