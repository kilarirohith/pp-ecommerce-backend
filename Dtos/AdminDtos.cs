using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ecommerce.Api.Dtos;

public class UpdateRoleRequest
{
    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "User";
}

public class PermissionsDto
{
    public bool CanManageProducts { get; set; }
    public bool CanViewAdminOrders { get; set; }
    public bool CanManageUsers { get; set; }
}
