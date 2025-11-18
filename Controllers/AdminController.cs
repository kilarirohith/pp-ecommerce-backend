using Ecommerce.Api.Data;
using Ecommerce.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("users")]
    public async Task<ActionResult<PaginatedResult<object>>> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        // Ensure page size doesn't exceed 50 for performance
        if (pageSize > 50) pageSize = 50;
        if (page < 1) page = 1;

        var query = _db.Users
            .Include(u => u.Permissions)
            .AsQueryable();

        // Apply search filter if provided
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim().ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(searchTerm) ||
                u.Email.ToLower().Contains(searchTerm) ||
                u.Role.ToLower().Contains(searchTerm));
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var users = await query
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = users.Select(u => new
        {
            u.Id,
            u.FullName,
            u.Email,
            u.Role,
            Permissions = u.Permissions == null ? null : new
            {
                u.Permissions.CanManageProducts,
                u.Permissions.CanViewAdminOrders,
                u.Permissions.CanManageUsers
            }
        });

        var paginatedResult = new PaginatedResult<object>
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

    [HttpPut("users/{id:int}/role")]
    public async Task<ActionResult> UpdateRole(int id, [FromBody] UpdateRoleRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (request.Role != "Admin" && request.Role != "User")
            return BadRequest("Invalid role");

        user.Role = request.Role;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("users/{id:int}/permissions")]
    public async Task<ActionResult<PermissionsDto>> GetPermissions(int id)
    {
        var user = await _db.Users.Include(u => u.Permissions)
            .SingleOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        var p = user.Permissions ?? new Models.UserPermissions();

        return new PermissionsDto
        {
            CanManageProducts = p.CanManageProducts,
            CanViewAdminOrders = p.CanViewAdminOrders,
            CanManageUsers = p.CanManageUsers
        };
    }

    [HttpPut("users/{id:int}/permissions")]
    public async Task<ActionResult> UpdatePermissions(int id, [FromBody] PermissionsDto request)
    {
        var user = await _db.Users.Include(u => u.Permissions)
            .SingleOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        if (user.Permissions == null)
        {
            user.Permissions = new Models.UserPermissions
            {
                UserId = user.Id
            };
            _db.UserPermissions.Add(user.Permissions);
        }

        user.Permissions.CanManageProducts = request.CanManageProducts;
        user.Permissions.CanViewAdminOrders = request.CanViewAdminOrders;
        user.Permissions.CanManageUsers = request.CanManageUsers;

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
