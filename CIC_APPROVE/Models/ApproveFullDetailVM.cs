using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CIC_APPROVE.Models
{
    public class ApproveFullDetailVM
    {
            public Vw_Approve Approve { get; set; }
            public List<Vw_ApproveIN> ApproveIN { get; set; }
            public List<Vw_ApproveOUT> ApproveOUT { get; set; }
    }

}