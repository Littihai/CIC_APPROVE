using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using CIC_APPROVE.Models;
using System.IO;
using System.Web;
namespace CIC_APPROVE.Controllers
{
    public class Vw_ApproveController : Controller
    {
        private CICCONTROL_SPRINGEntities2 db = new CICCONTROL_SPRINGEntities2();
        private TSGCORE_SPRINGEntities dbm = new TSGCORE_SPRINGEntities();
        // =========================
        // GET: Vw_Approve
        // =========================
        public ActionResult Index(string search, int page = 1)
        {
            int pageSize = 10;

            var query = db.Vw_Approve.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(x =>
                    x.CIC_No.Contains(search) ||
                    x.Name_user.Contains(search) ||
                    x.Dept_No.Contains(search)
                );
            }

            // Group ตาม Dept_No (เอา record ล่าสุด)
            var groupedQuery = query
                .GroupBy(x => x.Dept_No)
                .Select(g => g
                    .OrderByDescending(x => x.Create_Date)
                    .FirstOrDefault()
                )
                .Where(x => x != null);

            var totalCount = groupedQuery.Count();

            var data = groupedQuery
                .OrderByDescending(x => x.Create_Date)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.CurrentSearch = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPage = Math.Ceiling(totalCount / (double)pageSize);

            return View(data);
        }
        // =========================
        // APPROVE (หน้าเดียว)
        // =========================
        [HttpGet]
        public ActionResult Approve(string deptNo, string user, string token)
        {
            // ถ้าไม่มี token แต่มี deptNo + user → สร้าง token แล้ว redirect
            if (string.IsNullOrEmpty(token))
            {
                if (!string.IsNullOrEmpty(deptNo) && !string.IsNullOrEmpty(user))
                {
                    string raw = deptNo + "|" + user;
                    string encryptedToken = UrlEncryptionHelper.Encrypt(raw);
                    return RedirectToAction("Approve", new { token = encryptedToken });
                }

                return Content("Invalid Request");
            }

            string decrypted;

            try
            {
                decrypted = UrlEncryptionHelper.Decrypt(token);
            }
            catch
            {
                return Content("Token ไม่ถูกต้อง");
            }

            var parts = decrypted.Split('|');
            if (parts.Length != 2)
                return Content("Token Format ผิด");

            string finalDeptNo = parts[0];
            string finalUser = parts[1];

            // ====== ดึงข้อมูลเอกสาร ======
            var data = db.Vw_Approve
                         .Where(x => x.Dept_No == finalDeptNo)
                         .ToList();

            if (!data.Any())
                return Content("ไม่พบเอกสาร");

            // ====== ดึงข้อมูลผู้ใช้ ======
            var userProfile = db.UserProfiles
                                .FirstOrDefault(x => x.UserProfileLogon == finalUser);

            if (userProfile == null)
                return Content("ไม่พบผู้ใช้งาน");

            // ====== ส่งข้อมูลไป View ======
            ViewBag.Dept_No = finalDeptNo;
            ViewBag.ApproveUser = finalUser;
            ViewBag.UserTypeID = userProfile.UserTypeID;
            ViewBag.StatusID = data.First().StatusID ?? 0;

            // 🔥 สำคัญมาก: ส่งไฟล์แนบไป View ด้วย
            ViewBag.AttachList = db.trn_CICAttach
                                   .Where(x => x.AttachClosed != true)
                                   .Select(x => new
                                   {
                                       x.CICAttachID,
                                       x.CIC_ID,
                                       x.AttachFile
                                   })
                                   .ToList();

            return View(data);
        }
        public ActionResult OpenFile(long id)
        {
            var file = db.trn_CICAttach
                         .FirstOrDefault(x => x.CICAttachID == id && x.AttachClosed != true);

            if (file == null)
                return Content("File not found in database.");

            string fileName = Path.GetFileName(file.AttachPath);

            // Network Share
            string fullPath = @"\\tseacc\Attach\" + fileName;

            if (!System.IO.File.Exists(fullPath))
                return Content("Physical file not found on server: " + fullPath);

            string contentType = MimeMapping.GetMimeMapping(fullPath);

            // สำคัญมาก: บอก browser ให้เปิด inline
            Response.AppendHeader("Content-Disposition", "inline; filename=" + file.AttachFile);

            return File(fullPath, contentType);
        }
        // =========================
        // UPDATE STATUS
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateStatus(int StatusID, string Dept_No, string ApproveUser)
        {
            if (string.IsNullOrEmpty(Dept_No))
            {
                TempData["SwalError"] = "ข้อมูลไม่ถูกต้อง";
                return RedirectToAction("Approve", new { deptNo = Dept_No, user = ApproveUser });
            }

            var approve = db.trn_CIC.FirstOrDefault(x => x.Dept_No == Dept_No);

            if (approve == null)
            {
                TempData["SwalError"] = "ไม่พบเอกสาร";
                return RedirectToAction("Approve", new { deptNo = Dept_No, user = ApproveUser });
            }

            var loginUser = db.UserProfiles
                              .FirstOrDefault(x => x.UserProfileLogon == ApproveUser);

            if (loginUser == null)
            {
                TempData["SwalError"] = "ไม่พบผู้ใช้งานในระบบ";
                return RedirectToAction("Approve", new { deptNo = Dept_No, user = ApproveUser });
            }

            int userTypeId = loginUser.UserTypeID ?? 0;
            int oldStatus = approve.StatusID ?? 0;

            bool isValidFlow = false;

            // =====================================================
            // DEPT 11 → Flow เดิม (1→2→4)
            // =====================================================
            if (Dept_No.StartsWith("11"))
            {
                // Manager
                if (userTypeId == 2)
                {
                    if (oldStatus == 1 && StatusID == 2)
                        isValidFlow = true;

                    if (oldStatus == 2 && StatusID == 1)
                        isValidFlow = true;
                }
                // Accounting
                else if (userTypeId == 7)
                {
                    if (oldStatus == 2 && StatusID == 4)
                        isValidFlow = true;

                    if (oldStatus == 4 && StatusID == 1)
                        isValidFlow = true;
                }
                else
                {
                    TempData["SwalError"] = "ไม่มีสิทธิ์ดำเนินการ";
                    return RedirectToAction("Approve", new { deptNo = Dept_No, user = ApproveUser });
                }
            }

            // =====================================================
            // DEPT 12 / 13 / 14 → Flow ใหม่ (1→4)
            // =====================================================
            else if (Dept_No.StartsWith("12") ||
                     Dept_No.StartsWith("13") ||
                     Dept_No.StartsWith("14"))
            {
                // ไม่ใช่ Manager ห้ามทำ
                if (userTypeId != 2)
                {
                    TempData["SwalError"] = "ไม่มีสิทธิ์ดำเนินการ";
                    return RedirectToAction("Approve", new { deptNo = Dept_No, user = ApproveUser });
                }

                // Manager ทำได้แค่ 1→4 และ 4→1
                if (oldStatus == 1 && StatusID == 4)
                    isValidFlow = true;

                if (oldStatus == 4 && StatusID == 1)
                    isValidFlow = true;
            }
            else
            {
                TempData["SwalError"] = "ไม่รองรับประเภทเอกสารนี้";
                return RedirectToAction("Approve", new { deptNo = Dept_No, user = ApproveUser });
            }

            // =====================================================
            // Flow ไม่ถูกต้อง
            // =====================================================
            if (!isValidFlow)
            {
                TempData["SwalError"] = "ลำดับการอนุมัติไม่ถูกต้อง";
                return RedirectToAction("Approve", new { deptNo = Dept_No, user = ApproveUser });
            }

            // =====================================================
            // UPDATE DATA
            // =====================================================

            // APPROVE
            if (StatusID > oldStatus)
            {
                approve.Last_App = ApproveUser;
                approve.Last_App_update = DateTime.Now;

                approve.Cancel_User = null;
                approve.Cancel_Date = null;

                // เฉพาะ Dept 11 ที่ใช้ PO
                if (Dept_No.StartsWith("11") && userTypeId == 7)
                {
                    approve.Last_PO = ApproveUser;
                    approve.Last_PO_update = DateTime.Now;
                }
            }
            else // CANCEL
            {
                approve.Cancel_User = ApproveUser;
                approve.Cancel_Date = DateTime.Now;

                approve.Last_App = null;
                approve.Last_App_update = null;

                if (Dept_No.StartsWith("11"))
                {
                    approve.Last_PO = null;
                    approve.Last_PO_update = null;
                }
            }

            approve.StatusID = StatusID;

            db.SaveChanges();

            db.Send_CIC_Mail(Dept_No);

            TempData["SwalSuccess"] = "อัปเดตสถานะเรียบร้อยแล้ว";

            return RedirectToAction("Approve", new { deptNo = Dept_No, user = ApproveUser });
        }

        // ==============================
        // DETAILS PAGE
        // ==============================
        public ActionResult Detail(string deptNo, string user)
        {
            var data = db.Vw_ApproveIN
                         .Where(x => x.Dept_No == deptNo)
                         .ToList();

            ViewBag.DeptNo = deptNo;
            ViewBag.User = user;

            return View(data);
        }
        // =========================
        // APPROVE ALL
        // =========================
        [HttpPost]
        public ActionResult ApproveAll(string deptNo, string user)
        {
            var detailIds = db.Vw_ApproveIN
                              .Where(x => x.Dept_No == deptNo)
                              .Select(x => x.CICDetailID)
                              .ToList();

            // อนุมัติเฉพาะรายการที่ยังไม่ถูกอนุมัติ
            var records = db.trn_CIC14InList
                            .Where(x => x.CICDetailID.HasValue &&
                                        detailIds.Contains(x.CICDetailID.Value) &&
                                        x.FlagApp == false)
                            .ToList();

            if (records.Any())
            {
                var approveDate = DateTime.Now;

                foreach (var item in records)
                {
                    item.FlagApp = true;
                    item.UserApprove = user;
                    item.DateApprove = approveDate; // วันที่ของรอบนี้เท่านั้น
                }

                db.SaveChanges();
            }

            return RedirectToAction("Detail", new { deptNo = deptNo, user = user });
        }
        [HttpPost]
        public ActionResult CancelAll(string deptNo, string user)
        {
            var detailIds = db.Vw_ApproveIN
                              .Where(x => x.Dept_No == deptNo)
                              .Select(x => x.CICDetailID)
                              .ToList();

            var records = db.trn_CIC14InList
     .Where(x => x.CICDetailID.HasValue &&
                 detailIds.Contains(x.CICDetailID.Value) &&
                 x.FlagApp == true)
     .ToList();

            foreach (var item in records)
            {
                item.FlagApp = false;
                item.UserApprove = null;
                item.DateApprove = null;
            }

            db.SaveChanges();

            return RedirectToAction("Detail", new { deptNo = deptNo, user = user });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
                dbm.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
