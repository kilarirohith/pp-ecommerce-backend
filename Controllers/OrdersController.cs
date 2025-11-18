using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _db;

        public OrdersController(AppDbContext db)
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

        private bool IsAdmin() => User.IsInRole("Admin");

        // POST: api/orders
        [HttpPost]
        public async Task<ActionResult> Create()
        {
            var userId = GetUserId();

            var cart = await _db.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null || !cart.Items.Any())
                return BadRequest("Cart is empty");

            var order = new Order
            {
                UserId = userId,
                OrderDate = DateTime.UtcNow,
                Status = "Pending",
                TotalAmount = cart.Items.Sum(i => (i.Product?.Price ?? 0) * i.Quantity)
            };

            foreach (var item in cart.Items)
            {
                if (item.Product == null)
                    return BadRequest($"Product {item.ProductId} not found");

                if (item.Product.Stock < item.Quantity)
                    return BadRequest($"Insufficient stock for {item.Product.Name}");

                order.Items.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.Product.Price
                });

    
                item.Product.Stock -= item.Quantity;
            }

            _db.Orders.Add(order);
            _db.CartItems.RemoveRange(cart.Items); 
            await _db.SaveChangesAsync();

            return Ok(new { order.Id });
        }

        // GET: api/orders/my
       
[HttpGet("my")]
public async Task<ActionResult<PaginatedResult<OrderDto>>> MyOrders(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10)
{
    // Safety checks
    if (pageSize > 50) pageSize = 50;
    if (page < 1) page = 1;

    var userId = GetUserId();

    var query = _db.Orders
        .Include(o => o.Items)
        .ThenInclude(i => i.Product)
        .Where(o => o.UserId == userId);

    var totalCount = await query.CountAsync();
    var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

    var orders = await query
        .OrderByDescending(o => o.OrderDate)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    var items = orders.Select(o => new OrderDto
    {
        Id = o.Id,
        OrderDate = o.OrderDate,
        TotalAmount = o.TotalAmount,
        Status = o.Status,
        Items = o.Items.Select(i => new OrderItemDto
        {
            ProductId = i.ProductId,
            ProductName = i.Product?.Name ?? "",
            ProductImageUrl = i.Product?.ImageUrl,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            LineTotal = i.Quantity * i.UnitPrice
        }).ToList()
    }).ToList();

    var paginatedResult = new PaginatedResult<OrderDto>
    {
        Items = items,
        TotalCount = totalCount,
        Page = page,
        PageSize = pageSize,
        TotalPages = totalPages,
        HasNextPage = page < totalPages,
        HasPreviousPage = page > 1
    };

    return Ok(paginatedResult);
}


        // GET: api/orders/admin
        [HttpGet("admin")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<PaginatedResult<OrderDto>>> AdminOrders(
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            
            if (pageSize > 50) pageSize = 50;
            if (page < 1) page = 1;

            var query = _db.Orders
                .Include(o => o.User)
                .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                .AsQueryable();

            // Apply search filter if provided
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchTerm = search.Trim().ToLower();
                query = query.Where(o =>
                    o.Id.ToString().Contains(searchTerm) ||
                    o.User!.FullName.ToLower().Contains(searchTerm) ||
                    o.User!.Email.ToLower().Contains(searchTerm) ||
                    o.Status.ToLower().Contains(searchTerm) ||
                    o.Items.Any(i => i.Product!.Name.ToLower().Contains(searchTerm)));
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = orders.Select(o => new OrderDto
            {
                Id = o.Id,
                OrderDate = o.OrderDate,
                TotalAmount = o.TotalAmount,
                Status = o.Status,
                Items = o.Items.Select(i => new OrderItemDto
                {
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name ?? "",
                    ProductImageUrl = i.Product?.ImageUrl,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    LineTotal = i.Quantity * i.UnitPrice
                }).ToList()
            });

            var paginatedResult = new PaginatedResult<OrderDto>
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

        // DELETE: api/orders/{id}
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult> DeleteOrder(int id)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound();

            // Restore product stock
            foreach (var item in order.Items)
            {
                if (item.Product != null)
                {
                    item.Product.Stock += item.Quantity;
                }
            }

            _db.Orders.Remove(order);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
