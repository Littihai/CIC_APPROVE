using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CIC_APPROVE.Models
{
    public class Approve_Employee
    {
        // ----- CIC -----
        public long CIC_ID { get; set; }
        public string CIC_No { get; set; }
        public int? StatusID { get; set; }
        public string StatusName { get; set; }
        public DateTime? Date_approve { get; set; }

        // ----- Employee -----
        public string EmployeeCode { get; set; }
        public string EmployeeUsername { get; set; }
        public string FullNameTH { get; set; }
        public string DepartmentCode { get; set; }
        public string Email { get; set; }
    }
}