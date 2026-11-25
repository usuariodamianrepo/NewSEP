using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using SEP.Shared.DTOs;
using SEP.WebApiEtikit.Labeling;

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
        private readonly ILabelSvgService _svgService;

        public ProductsController(ILogger<ProductsController> logger, ILabelSvgService svgService)
        {
            _logger = logger;
            _svgService = svgService;
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
                return ErrorResult(StatusCodes.Status400BadRequest, "El producto no tiene código ZPL.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var svg = _svgService.RenderZpl(product.ZplCode);

                return Content(svg, "image/svg+xml; charset=utf-8");
            }
            catch (Exception ex) when (ex is LabelParseException or LabelRenderException)
            {
                _logger.LogError(ex, "No se pudo interpretar el ZPL del producto {ProductId}.", id);

                return ErrorResult(
                    StatusCodes.Status422UnprocessableEntity,
                    "No se pudo interpretar el código ZPL de la etiqueta.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo renderizar la etiqueta del producto {ProductId}.", id);

                return ErrorResult(
                    StatusCodes.Status500InternalServerError,
                    "No se pudo generar la imagen SVG de la etiqueta.");
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
                return ErrorResult(StatusCodes.Status400BadRequest, "El producto no tiene código TPCL.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var svg = _svgService.RenderTpcl(product.TpclCode);

                return Content(svg, "image/svg+xml; charset=utf-8");
            }
            catch (Exception ex) when (ex is LabelParseException or LabelRenderException)
            {
                _logger.LogError(ex, "No se pudo interpretar el TPCL del producto {ProductId}.", id);

                return ErrorResult(
                    StatusCodes.Status422UnprocessableEntity,
                    "No se pudo interpretar el código TPCL de la etiqueta.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo generar la imagen TPCL del producto {ProductId}.", id);

                return ErrorResult(
                    StatusCodes.Status500InternalServerError,
                    "No se pudo generar la imagen SVG de la etiqueta.");
            }
        }

        private static IActionResult ErrorResult(int statusCode, string message) =>
            new ContentResult
            {
                StatusCode = statusCode,
                Content = message,
                ContentType = "text/plain; charset=utf-8"
            };

        private static async Task<List<Product>> ReadProductsAsync()
        {
            var jsonContent = await System.IO.File.ReadAllTextAsync("Data/products.json");

            return JsonSerializer.Deserialize<List<Product>>(jsonContent, JsonOptions) ?? new List<Product>();
        }
    }
}
