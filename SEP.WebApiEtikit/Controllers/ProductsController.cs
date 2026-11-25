using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using SEP.Shared.DTOs;

namespace SEP.WebApiEtikit.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly ILogger<ProductsController> _logger;

        public ProductsController(ILogger<ProductsController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductDto>>> Get()
        {
            List<ProductDto> result = new();
            var products = await ReadProductsAsync();
            foreach (var product in products)
            {
                var item = new ProductDto(product.Id, product.Name, product.Size, product.Description, product.Price);
                result.Add(item);
            }
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> Get(int id)
        {
            var products = await ReadProductsAsync();

            var product = products.FirstOrDefault(p => p.Id == id);
            if (product == null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        [HttpGet("{id}/zpl-image")]
        public async Task<IActionResult> GetZplImage(
            int id,
            CancellationToken cancellationToken)
        {
            var products = await ReadProductsAsync();

            var product = products.FirstOrDefault(p => p.Id == id);
            if (product == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(product.ZplCode))
            {
                return BadRequest("El producto no tiene código ZPL.");
            }

            try
            {
                //TODO: Implementar el render de la imagen para product.ZplCode

                return File(png, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo renderizar la etiqueta del producto {ProductId}.", id);

                return StatusCode(StatusCodes.Status502BadGateway, "No se pudo generar la imagen de la etiqueta.");
            }
        }

        [HttpGet("{id}/tpcl-image")]
        public async Task<IActionResult> GetTpclImage(
            int id,
            CancellationToken cancellationToken)
        {
            var products = await ReadProductsAsync();

            var product = products.FirstOrDefault(p => p.Id == id);
            if (product == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(product.TpclCode))
            {
                return BadRequest("El producto no tiene código TPCL.");
            }

            try
            {
                //TODO: Implementar el render de la imagen para product.TpclCode
                return File(png, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo generar la imagen TPCL del producto {ProductId}.", id);

                return StatusCode(StatusCodes.Status502BadGateway, "No se pudo generar la imagen de la etiqueta.");
            }
        }

        private static async Task<List<Product>> ReadProductsAsync()
        {
            var jsonContent = await System.IO.File.ReadAllTextAsync("Data/products.json");

            return JsonSerializer.Deserialize<List<Product>>(jsonContent, JsonOptions) ?? new List<Product>();
        }
    }
}
