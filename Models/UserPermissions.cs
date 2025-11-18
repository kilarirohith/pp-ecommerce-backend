using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ecommerce.Api.Models;

public class UserPermissions
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    public bool CanManageProducts { get; set; }

    public bool CanViewAdminOrders { get; set; }

    public bool CanManageUsers { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
