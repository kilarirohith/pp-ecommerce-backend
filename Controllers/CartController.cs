using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
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
    [Authorize]
    public class CartController : ControllerBase
    {
        private readonly AppDbContext _db;

        public CartController(AppDbContext db)
        {
            _db = db;
        }

        private int GetUserId()
        {
            var id =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(id))
                throw new Exception("User id claim is missing in token.");

            return int.Parse(id);
        }

        // DTO returned by GetCart (no pagination)
        public class CartDto
        {
            public int CartId { get; set; }
            public CartItemDto[] Items { get; set; } = Array.Empty<CartItemDto>();
            public decimal TotalAmount { get; set; }
        }

        // GET: api/cart
        // Returns the full cart for the authenticated user (no paging).
        [HttpGet]
        public async Task<ActionResult<CartDto>> GetCart([FromQuery] string? search = null)
        {
            if (!string.IsNullOrWhiteSpace(search))
                search = search.Trim().ToLower();

            var userId = GetUserId();

            // Ensure cart exists
            var cart = await _db.Carts.FirstOrDefaultAsync(c => c.UserId == userId);
            if (cart == null)
            {
                cart = new Cart { UserId = userId };
                _db.Carts.Add(cart);
                await _db.SaveChangesAsync();
            }

            // Query CartItems directly from DbSet so EF async methods work correctly.
            var query = _db.CartItems
                .Include(i => i.Product)
                .Where(i => i.CartId == cart.Id);

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(i =>
                    (i.Product != null && i.Product.Name != null && i.Product.Name.ToLower().Contains(search)) ||
                    (i.Product != null && i.Product.Description != null && i.Product.Description.ToLower().Contains(search)));
            }

            var items = await query
                .OrderBy(i => i.Id)
                .ToListAsync();

            var cartItemDtos = items.Select(i => new CartItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product != null ? i.Product.Name ?? string.Empty : string.Empty,
                ProductPrice = i.Product != null ? i.Product.Price : 0m,
                Quantity = i.Quantity,
                LineTotal = (i.Product != null ? i.Product.Price : 0m) * i.Quantity,
                ProductImageUrl = i.Product != null ? i.Product.ImageUrl : null
            }).ToArray();

            var totalAmount = cartItemDtos.Sum(ci => ci.LineTotal);

            var result = new CartDto
            {
                CartId = cart.Id,
                Items = cartItemDtos,
                TotalAmount = totalAmount
            };

            return Ok(result);
        }

        // POST: api/cart/add
        [HttpPost("add")]
        public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
        {
            var userId = GetUserId();

            var product = await _db.Products.FindAsync(request.ProductId);
            if (product == null) return NotFound("Product not found");

            if (product.Stock < request.Quantity)
                return BadRequest("Insufficient stock");

            var cart = await _db.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null)
            {
                cart = new Cart { UserId = userId };
                _db.Carts.Add(cart);
                await _db.SaveChangesAsync();
            }

            var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);
            if (existingItem != null)
            {
                existingItem.Quantity += request.Quantity;
            }
            else
            {
                cart.Items.Add(new CartItem
                {
                    ProductId = request.ProductId,
                    Quantity = request.Quantity
                });
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // PUT: api/cart/update/{itemId}
        [HttpPut("update/{itemId:int}")]
        public async Task<IActionResult> UpdateCartItem(int itemId, [FromBody] UpdateCartItemRequest request)
        {
            var userId = GetUserId();

            var item = await _db.CartItems
                .Include(i => i.Cart)
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.Cart!.UserId == userId);

            if (item == null) return NotFound("Cart item not found.");

            if (item.Product == null)
                return BadRequest("Product not found.");

            if (item.Product.Stock < request.Quantity)
                return BadRequest("Insufficient stock");

            item.Quantity = request.Quantity;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/cart/remove/{itemId}
        [HttpDelete("remove/{itemId:int}")]
        public async Task<IActionResult> RemoveFromCart(int itemId)
        {
            var userId = GetUserId();

            var item = await _db.CartItems
                .Include(i => i.Cart)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.Cart!.UserId == userId);

            if (item == null) return NotFound("Cart item not found.");

            _db.CartItems.Remove(item);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/cart/clear
        [HttpDelete("clear")]
        public async Task<IActionResult> ClearCart()
        {
            var userId = GetUserId();

            var cart = await _db.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null) return NotFound("Cart not found.");

            _db.CartItems.RemoveRange(cart.Items);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}