using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CIC_APPROVE.Models
{
    [Table("UserRoles")]
    public class UserRoles
    {
        [Key]
        public long UserRolesID { get; set; }   //แก้จาก int → long

        public string UserProfileLogon { get; set; }
        public string CompGroupCode { get; set; }
        public string CompanyCode { get; set; }
        public string PlantCode { get; set; }
        public string DepartmentCode { get; set; }
        public string DivisionCode { get; set; }
        public string Costcenter { get; set; }

        public DateTime? LastDateTime { get; set; }
    }
}
