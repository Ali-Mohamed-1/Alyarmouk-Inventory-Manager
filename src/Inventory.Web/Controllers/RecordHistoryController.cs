using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Infrastructure.Data;
using Inventory.Domain.Entities;
using Inventory.Web.Models;

namespace Inventory.Web.Controllers
{
    [Authorize]
    public class RecordHistoryController : Controller
    {
        private readonly AppDbContext _db;

        public RecordHistoryController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var settings = await _db.BankSystemSettings.AsNoTracking().FirstOrDefaultAsync();
            var model = new BankSettingsViewModel
            {
                BankBaseBalance = settings?.BankBaseBalance ?? 0m,
                LastUpdatedUtc = settings?.LastUpdatedUtc
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBankBalance(BankSettingsViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("Index", model);
            }

            var settings = await _db.BankSystemSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new BankSystemSettings();
                _db.BankSystemSettings.Add(settings);
            }

            settings.BankBaseBalance = model.BankBaseBalance;
            settings.LastUpdatedUtc = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "Starting bank balance updated successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
