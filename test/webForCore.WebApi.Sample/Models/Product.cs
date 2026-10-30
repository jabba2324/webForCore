using System.ComponentModel.DataAnnotations;

namespace WebApiSample.Models
{
    // From "Getting Started with ASP.NET Web API 2", with validation attributes for the model-state checks.
    public class Product
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string Name { get; set; }

        public string Category { get; set; }

        [Range(0, 1000)]
        public decimal Price { get; set; }
    }
}
