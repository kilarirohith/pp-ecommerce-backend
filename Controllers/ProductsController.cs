using Ecommerce.Api.Data;
using Ecommerce.Api.Dtos;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Route("[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public ProductsController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<PaginatedResult<ProductDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12)
        {
            
            if (pageSize > 50) pageSize = 50;
            if (page < 1) page = 1;

            var query = _db.Products.AsQueryable();

            
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchTerm = search.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(searchTerm) ||
                    p.Description.ToLower().Contains(searchTerm));
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var products = await query
                .OrderBy(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = products.Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Stock = p.Stock,
                ImageUrl = p.ImageUrl
            });

            var paginatedResult = new PaginatedResult<ProductDto>
            {
                Items = result,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                HasNextPage = page < totalPages,
                HasPreviousPage = page > 1
            };

            return Ok(paginatedResult);
        }

        
        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<ActionResult<ProductDto>> Get(int id)
        {
            var p = await _db.Products.FindAsync(id);
            if (p == null) return NotFound();

            return new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Stock = p.Stock,
                ImageUrl = p.ImageUrl
            };
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<ProductDto>> Create([FromForm] CreateProductDto request)
        {
            var product = new Product
            {
                Name = request.Name,
                Description = request.Description,
                Price = request.Price,
                Stock = request.Stock
            };

            if (request.ImageFile is not null && request.ImageFile.Length > 0)
            {
                product.ImageUrl = await SaveImageAsync(request.ImageFile);
            }

            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            var dto = new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Stock = product.Stock,
                ImageUrl = product.ImageUrl
            };

            return CreatedAtAction(nameof(Get), new { id = product.Id }, dto);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Update(int id, [FromForm] UpdateProductDto request)
        {
            var product = await _db.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.Name = request.Name;
            product.Description = request.Description;
            product.Price = request.Price;
            product.Stock = request.Stock;

            if (request.ImageFile is not null && request.ImageFile.Length > 0)
            {
                //  delete old image 
                 TryDeleteOldImage(product.ImageUrl);

                product.ImageUrl = await SaveImageAsync(request.ImageFile);
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _db.Products
                .Include(p => p.OrderItems)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null) return NotFound();

            // Check if product is referenced in any orders
            if (product.OrderItems.Any())
            {
                return BadRequest(new
                {
                    message = "Cannot delete product because it is referenced in existing orders. " +
                             "Consider setting stock to 0 instead to prevent new purchases."
                });
            }

            // Delete associated image if exists
            TryDeleteOldImage(product.ImageUrl);

            _db.Products.Remove(product);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        private async Task<string> SaveImageAsync(IFormFile file)
        {
            var extension = Path.GetExtension(file.FileName);
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            if (!allowedExtensions.Contains(extension.ToLower()))
            {
                throw new InvalidOperationException("Invalid file type. Only JPG, JPEG, PNG, WEBP are allowed.");
            }

            var webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var imagesFolder = Path.Combine(webRootPath, "images");

            if (!Directory.Exists(imagesFolder))
                Directory.CreateDirectory(imagesFolder);

            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(imagesFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var imageUrl = $"{baseUrl}/images/{fileName}";
            return imageUrl;
        }

        // delete old images
        private void TryDeleteOldImage(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            var uri = new Uri(imageUrl);
            var fileName = Path.GetFileName(uri.LocalPath);

            var webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var imagesFolder = Path.Combine(webRootPath, "images");
            var filePath = Path.Combine(imagesFolder, fileName);

            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }
    }
}
