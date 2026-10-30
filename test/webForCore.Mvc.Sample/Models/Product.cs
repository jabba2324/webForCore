using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MvcSample.Models
{
    public class Product
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string Name { get; set; }

        [Range(0, 1000)]
        public decimal Price { get; set; }

        public List<string> Tags { get; set; }

        public Address Supplier { get; set; }

        public override string ToString() => $"{Id}:{Name}:{Price}";
    }

    public class Address
    {
        public string City { get; set; }
        public string Country { get; set; }
    }
}
