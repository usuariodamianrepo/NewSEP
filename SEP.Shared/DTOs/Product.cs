namespace SEP.Shared.DTOs
{
    public sealed class Product
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Size { get; set; } = string.Empty;

        public string? Description { get; set; }

        public decimal Price { get; set; }

        public string ZplCode { get; set; } = string.Empty;
        public string TpclCode { get; set; } = string.Empty;
    }

    public sealed record class ProductDto(int Id, string Name, string Size, string? Description, decimal Price);
}
