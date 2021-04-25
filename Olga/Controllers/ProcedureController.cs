﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using AutoMapper;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Olga.AutoMapper;
using Olga.BLL.DTO;
using Olga.BLL.Interfaces;
using Olga.DAL.Entities;
using Olga.Models;
using Olga.Util;
using PagedList;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Newtonsoft.Json;

namespace Olga.Controllers
{
    [Authorize]
    public class ProcedureController : Controller
    {
        ICountry _countryService;
        IProductService _productService;
        readonly IBaseEmailService _emailService;
        IProcedure _procedureService;
        bool toSend = bool.Parse(WebConfigurationManager.AppSettings["makeNotificationProc"]);
        Emailer emailer;
        private UserViewModel _currentUser;
        IArchProccessor _archProccessor;
        static object locker = new object();


        public ProcedureController(ICountry serv,  IProductService product, IProcedure procedure, IBaseEmailService emailService, IArchProccessor archProccessor)
        {
            _countryService = serv;
            _productService = product;
            _emailService = emailService;
            _procedureService = procedure;
            _archProccessor = archProccessor;
            emailer = new Emailer()
            {
                Login = WebConfigurationManager.AppSettings["login"],
                Pass = WebConfigurationManager.AppSettings["password"],
                From = WebConfigurationManager.AppSettings["from"],
                Port = int.Parse(WebConfigurationManager.AppSettings["smtpPort"]),
                SmtpServer = WebConfigurationManager.AppSettings["smtpSrv"],
                DirectorMail = WebConfigurationManager.AppSettings["directorMail"],
                DeveloperMail = WebConfigurationManager.AppSettings["developerMail"],
            };
        }
        // GET: Procedure
        public ActionResult Index(int? countryId)
        {
            if (countryId == 0 || countryId == null)
            { 
                ViewBag.Error = Resources.ErrorMessages.NoIdInRequest;
                return View("Error");
            }

            if (!InitialiseModel(countryId, out var errorMessage))
            {
                ViewBag.Error = errorMessage;
                return View("Error");
            }

            _currentUser = GetCurrentUser();
            /*Todo add to check && !User.IsInRole("Holder")*/
            if (_currentUser.Countries.All(a => a.Id != countryId) && !User.IsInRole("Admin") )
            {
                ViewBag.Error = Resources.ErrorMessages.NoPermission;
                return View("Error");
            }
            ViewBag.User = _currentUser;

            var allProcedures = GetProcedures((int)countryId);
            return View(allProcedures);
        }

        public ActionResult AllProcedures(int? country, string dateFrom, string dateTo)
        {
            if (!User.IsInRole("Admin"))
            {
                return View("Index");
            }
            var allProcedures = new List<ProcedureViewModel>();
            var _allProcedures = _procedureService.GetItems();
            
            if (country != null && country != 0)
            {
                _allProcedures = _allProcedures.Where(p => p.Product.Country.Id == country);
            }
            if (!string.IsNullOrEmpty(dateFrom) || !string.IsNullOrEmpty(dateTo))
            {
                var _dateFrom = string.IsNullOrEmpty(dateFrom) ? DateTime.MinValue : DateTime.Parse(dateFrom);
                var _dateTo = string.IsNullOrEmpty(dateTo) ? DateTime.MaxValue : DateTime.Parse(dateTo);
                _allProcedures = _allProcedures.Where(p => p.SubmissionDate >= _dateFrom && p.SubmissionDate <= _dateTo);
            }

            var procedures = Mapper.Map<IEnumerable<ProcedureDTO>, IEnumerable<ProcedureViewModel>>(_allProcedures).ToList();
            allProcedures.AddRange(procedures);

            var countries = _countryService.GetItems().OrderBy(a => a.Name).ToList();
            countries.Insert(0, new CountryDTO() { Name = "All", Id = 0 });
            ViewBag.Countries = new SelectList(countries, "Id", "Name");
            return View(allProcedures);
        }


        public bool InitialiseModel(int? countryId, out string errorMessage)
        {
            errorMessage = string.Empty;
            var country = Mapper.Map<CountryDTO, CountryViewModel>(_countryService.GetItem((int)countryId));
            @ViewBag.CountryName = country.Name;
            @ViewBag.CountryId = countryId;
            var allProducts = _productService.GetProducts(countryId);
            if (allProducts == null)
            {
                errorMessage = $"{country.Name} {Resources.ErrorMessages.NoProcCauseNoProd}";
                return false;
            }
            List<SelectListItem> products = 
                allProducts.Select(n => new SelectListItem { Text = n.ProductName?.Name ?? "No name", Value = n.Id.ToString() }).ToList();
            @ViewBag.Products = products;
            return true;
        }

        public List<ProcedureViewModel> GetProcedures(int countryId)
        {
            var productsDto = _productService.GetProducts(countryId);
            if (User.IsInRole("Holder"))
            {
                return null;
                //Todo add productsDto = productsDto.Where(a => a.MarketingAuthorizHolderId == _currentUser.MarketingAuthorizHolder.Id).ToList();
            }

            var allProducts = Mapper.Map<IEnumerable<ProductDTO>, List<ProductViewModel>>(productsDto).ToArray();
            var allProcedures = new List<ProcedureViewModel>();

            foreach (var product in allProducts)
            {
                var proc = _procedureService.GetItems().Where(a => a.ProductId == product.Id);
                var procedureDtos = proc as ProcedureDTO[] ?? proc.ToArray();
                if(!procedureDtos.Any()) continue;
                var procedures = Mapper.Map<IEnumerable<ProcedureDTO>, IEnumerable<ProcedureViewModel>>(procedureDtos).ToList();
                allProcedures.AddRange(procedures);
            }
            return allProcedures;
        }
       
        /*----------------------------------------------------------------------------*/

        private IUserService UserService => HttpContext.GetOwinContext().GetUserManager<IUserService>();

        [HttpGet]
        public async Task<ActionResult> ProductProcedures(int id)
        {
            //Todo delete this check
            if (User.IsInRole("Holder"))
            {
                @ViewBag.Error = Resources.ErrorMessages.NoPermission;
                return View("Error");
            }

            if (id == 0)
            {
                @ViewBag.Error = Resources.ErrorMessages.NoIdInRequest;
                return View("Error");
            }

            ViewBag.ProductId = id;
            _currentUser = GetCurrentUser();

            var proceduresListDto = _procedureService.GetItems(id);
            var procedureViewModelList = Mapper.Map<IEnumerable<ProcedureDTO>, IList<ProcedureViewModel>>(proceduresListDto);

            var countryId = proceduresListDto.First().Product.CountryId;
            var marketingAuthorizHolderName = proceduresListDto.First().Product.MarketingAuthorizHolder.Name;

            if (_currentUser.Countries.All(a => a.Id != countryId) && !User.IsInRole("Admin") && !User.IsInRole("Holder"))
            {
                @ViewBag.Error = Resources.ErrorMessages.NoPermission;
                return View("Error");
            }
            if (User.IsInRole("Holder") && !marketingAuthorizHolderName.Equals(_currentUser.MarketingAuthorizHolder.Name))
            {
                @ViewBag.Error = Resources.ErrorMessages.NoPermission;
                return View("Error");
            }

            return View(procedureViewModelList);
        }

        [HttpGet]
        public ActionResult CreateProcedure(int id)
        {
            if (id == 0)
            {
                @ViewBag.Error = "No productId in request!";
                return View("Error");
            }
            try
            {
                var model = new ProcedureViewModel();
                _currentUser = GetCurrentUser();
                model.ProductId = id;

                return View(model);
            }
            catch (Exception e)
            {
                @ViewBag.Error = e.Message;
                Logger.Log.Error($"#{nameof(CreateProcedure)}: {e.Message}");
                return View("Error");
            }
        }

        [HttpPost]
        public async Task<ActionResult> CreateProcedure(ProcedureViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Error = CreateError();
                return View("Error");
            }
            try
            {
                var procedureDto = Mapper.Map<ProcedureViewModel, ProcedureDTO>(model);
                _procedureService.AddItem(procedureDto);
                _procedureService.Commit();

                TempData["Success"] = Resources.Messages.ProcedureCreatedSuccess;
                await SenEmailAboutAddProcedure(model);
                _currentUser = GetCurrentUser();
                Logger.Log.Info($"{_currentUser.Email} Added Procedure for Product #{procedureDto.ProductId} {procedureDto.ProcedureType}");
                return RedirectToAction("ProductProcedures", new { id = model.ProductId });
            }
            catch (Exception ex)
            {
                @ViewBag.Error = ex.ToString();
                Logger.Log.Error($"#{nameof(CreateProcedure)}: {ex.Message}");
                return View("Error");
            }
        }

        // GET: Procedure/Delete/5
        public ActionResult DeleteProcedure(int id, int productId)
        {
            if (id == 0 || productId==0)
            {
                @ViewBag.Error = "Error happened in DeleteProcedure method: no Id in GET request.";
                return View("Error");
            }
            try
            {
                var procedureDocs = _procedureService.GetItem(id).ProcedureDocuments.ToList();
                var targetFolder = Server.MapPath($"~/Upload/Documents/Procedures/");

                foreach (var doc in procedureDocs)
                {
                    DeleteFile(doc.PathToDocument, targetFolder);
                }
                _procedureService.DeleteItem(id);
                _procedureService.Commit();
                _currentUser = GetCurrentUser();
                TempData["Success"] = Resources.Messages.ProcedureDeletedSuccess;
                Logger.Log.Info($"{_currentUser.Email} Deleted Procedure #{id} for Product #{productId} ");

                return RedirectToAction("ProductProcedures", new { id = productId });
            }
            catch (Exception ex)
            {
                @ViewBag.Error = ex.Message;
                Logger.Log.Error($"#{nameof(DeleteProcedure)}: {ex.Message}");
                return View("Error");
            }
        }

        [HttpGet]
        public async Task<ActionResult> EditProcedure(int id, int? productId)
        {
            if (id==0 || productId == 0)
            {
                @ViewBag.Error = nameof(id);
                return View("Error");
            }
            try
            {
                var procedure = await _procedureService.GetItemAsync(id);
                var procedureDto = Mapper.Map<ProcedureDTO, ProcedureEditModel>(procedure);

                procedureDto.ProductId = id;

                return View(procedureDto);
            }
            catch (Exception e)
            {
                @ViewBag.Error = e.Message;
                Logger.Log.Error($"#{nameof(EditProcedure)}: {e.Message}");
                return View("Error");
            }
        }

        [HttpPost]
        public async Task<ActionResult> EditProcedure(ProcedureEditModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Error = CreateError();
                return View("Error");
            }
            try
            {
                var procedureDto = Mapper.Map<ProcedureEditModel, ProcedureDTO>(model);

                _procedureService.Update(procedureDto);
                _procedureService.Commit();
               
                _currentUser = GetCurrentUser();
                TempData["Success"] = Resources.Messages.ProcedureUpdatedSuccess;
                
                Logger.Log.Info($"{_currentUser.Email} Updated/Edited Procedure #{model.Id} for Product #{model.ProductId} ");

                await SenEmailAboutUpdateProcedureAsync(model);

                return RedirectToAction("ProductProcedures", new { id = model.ProductId });
            }
            catch (Exception ex)
            {
                @ViewBag.Error = ex.ToString();
                Logger.Log.Error($"#{nameof(EditProcedure)}: {ex.Message}");
                return View("Error");
            }
        }

        [HttpGet]
        public ActionResult EditFiles(int id, int? productId, ProcedureDocsType procedureDocsType)
        {
            if (id == 0 || productId == 0)
            {
                @ViewBag.Error = nameof(id);
                return View("Error");
            }
            try
            {
                var procedure = _procedureService.GetItem(id);
                var procedureDto = Mapper.Map<ProcedureDTO, ProcedureViewModel>(procedure);

                _currentUser = GetCurrentUser();

                procedureDto.ProductId = (int)productId;
                ViewBag.ProcedureDocsType = procedureDocsType;
                ViewBag.User = _currentUser;
                ViewBag.DocsType = Enum.GetValues(typeof(ProcedureDocsType));
                return View(procedureDto);
            }
            catch (Exception ex)
            {
                @ViewBag.Error = ex.ToString();
                Logger.Log.Error($"#{nameof(EditFiles)}: {ex.Message}");
                return View("Error");
            }
        }

        [HttpPost]
        public async Task<ActionResult> GetProductInfo(int productId)
        {
            try
            {
                var productDto = await _productService.FindAsync(productId);
                var productViewModel = Mapper.Map<ProductDTO, ProductViewModel>(productDto);

                var responseModel = new { 
                    pharmaceuticalForm = productViewModel.PharmaceuticalForm,
                    strength= productViewModel.Strength,
                    marketingAuthorizNumber = productViewModel.MarketingAuthorizNumber,
                    productCode = productViewModel.ProductCode,
                    productName = productViewModel.ProductName,
                    countryId = productDto.CountryId,
                    flagSrc = $"{productDto.CountryId}.gif",
                    countryName = productDto.Country.Name
                };

                string json = JsonConvert.SerializeObject(responseModel);

                return Json(new {success = true, responseText = json}, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Cannot get product info: {ex.Message}");
                return Json(new { success = false, responseText = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EditProcedureFiles(IEnumerable<HttpPostedFileBase> uploadFiles, string procedureDocsType, string procedureId, string productId)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Error = CreateError();
                return View("Error");
            }
            try
            {
                if (uploadFiles == null) return Json(new { success = false, responseText = "uploads == null!" }, JsonRequestBehavior.AllowGet);

                foreach (var file in uploadFiles)
                {
                    if (file == null || file.ContentLength <= 0) continue;
                    var targetFolder = Server.MapPath($"~/Upload/Documents/Procedures/");

                    if (!SaveHttpPostedFile(file, ref targetFolder, out string targetPath, out string localFileName)) continue;

                    var fileExt = Path.GetExtension(localFileName);

                    var procId = int.Parse(procedureId);
                    var procDocType = int.Parse(procedureDocsType);

                    if (fileExt.Equals(".zip"))
                    {
                        var filesFromArchive = _archProccessor.ProcessArchive(targetPath, targetFolder);
                        if ((filesFromArchive.Count == 1 && filesFromArchive[0].Contains("Error")) || filesFromArchive.Count == 0)
                        {
                            Logger.Log.Error($"Error: Archive {localFileName} wasn't processed! {filesFromArchive[0] ?? string.Empty} ");
                            return Json(new { success = false, responseText = filesFromArchive[0] ?? $"Error in abstraction archive {localFileName}" }, JsonRequestBehavior.AllowGet);
                        }
                        foreach (var fileFromArchive in filesFromArchive)
                        {
                            AddFileToProc(fileFromArchive, procId, procDocType);
                        }
                        continue;
                    }
                    AddFileToProc(localFileName, procId, procDocType);
                }
                return Json(new { success = true, responseText = $"File processed successfully!" }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"EditProcedureFiles: {ex.Message}");
                return Json(new { success = false, responseText = $"{ex}" }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public ActionResult EditProcedureFiles(int id, int? productId)
        {
            if (id == 0 || productId == 0)
            {
                @ViewBag.Error = nameof(id);
                return View("Error");
            }
            try
            {
                var procedure = _procedureService.GetItem(id);
                var procedureDto = Mapper.Map<ProcedureDTO, ProcedureViewModel>(procedure);

                var productDto = _productService.GetProduct((int)productId);
                var product = Mapper.Map<ProductDTO, ProductViewModel>(productDto); 
                _currentUser = GetCurrentUser();

                procedureDto.ProductId = (int)productId;
                ViewBag.Country = product.Country;
                ViewBag.CountryId = productDto.CountryId;
                ViewBag.Product = product;
                ViewBag.User = _currentUser;
                ViewBag.DocsType = Enum.GetValues(typeof(ProcedureDocsType));

                return View(procedureDto);
            }
            catch (Exception ex)
            {
                @ViewBag.Error = ex.ToString();
                Logger.Log.Error($"#{nameof(EditProcedureFiles)}: {ex.Message}");
                return View("Error");
            }
        }

        public bool SaveHttpPostedFile(HttpPostedFileBase file, ref string targetFolder, out string targetPath, out string localFileName)
        {
            var fileTrimmName = file.FileName.TrimFileName();
            try
            {
                var fileExt = Path.GetExtension(fileTrimmName);
                targetFolder = fileExt.Equals(".zip") ? Server.MapPath($"~/Upload/Documents/Procedures/Archives/") : targetFolder;

                localFileName = $"{Path.GetFileNameWithoutExtension(fileTrimmName)}_{Guid.NewGuid().ToString().Substring(0, 6)}{fileExt}";
                targetPath = Path.Combine(targetFolder, localFileName);
                file.SaveAs(targetPath);
                _currentUser = GetCurrentUser();
                Logger.Log.Info($"{_currentUser.Email} Downloaded file {localFileName}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Cannot save file {fileTrimmName} in {targetFolder}: {ex}");
                targetPath = localFileName = String.Empty;
                return false;
            }
        }

        public void AddFileToProc(string localFileName, int procedureId, int procDocType)
        {
            try
            {
                var doc = new ProcedureDocument()
                {
                    PathToDocument = localFileName,
                    ProcedureId = procedureId,
                    ProcedureDocsType = (ProcedureDocsType) procDocType
                };
                //await SendEmailAboutAddFileProcedure(procedureId, productId, (ProcedureDocsType)procDocType, localFileName);
                var procedure = _procedureService.GetItem(procedureId);
                procedure.ProcedureDocuments.Add(doc);
                _procedureService.Update(procedure);
                _currentUser = GetCurrentUser();
                Logger.Log.Info($"{_currentUser.Email} Added file {localFileName}");
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Cannot add file {localFileName} to Procedure: {ex}");
            }
        }

        [HttpPost]
        public void DeleteProcedureFile(string documentId, int procedureId)
        {
            if (documentId == null || procedureId == 0 ) throw new ArgumentNullException();
            var targetFolder = Server.MapPath($"~/Upload/Documents/Procedures/");
            var procedure = _procedureService.GetItem(procedureId);
            var documentID = int.Parse(documentId);
            var document = procedure.ProcedureDocuments.FirstOrDefault(a => a.Id == documentID);
            var deleteRes = DeleteFile(document.PathToDocument, targetFolder);
            if (deleteRes)
            {
                //procedure.ProcedureDocuments.Remove(document);
                //_procedureService.Update(procedure);
                _procedureService.DeleteDocument(document.PathToDocument);
            }
        }

        [HttpPost]
        public async Task<ActionResult> AddProcedureFileToArchive(string documentID, string isArchiveValue)
        {
            try
            {
                if (string.IsNullOrEmpty(documentID))
                {
                    return Json(new { success = false, responseText = $"#AddProcedureFileToArchive empty parameters" }, JsonRequestBehavior.AllowGet);
                }

                int.TryParse(documentID, out var documentId);
                bool.TryParse(isArchiveValue, out var isArchive);

                isArchive = !isArchive;

                await _procedureService.AddDocumentToArchive(documentId, isArchive);

                var resultText = isArchive ? "File added to archive!" : "File extracted from archive!";

                return Json(new { success = true, responseText = resultText }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, responseText = $"{ex}" }, JsonRequestBehavior.AllowGet);
            }
        }

        /*----------------------------------------------------------------------------*/
        public string CreateError()
        {
            var errorMessage = new StringBuilder();
            foreach (ModelState modelState in ViewData.ModelState.Values)
            {
                foreach (ModelError error in modelState.Errors)
                {
                    errorMessage.Append(error.Exception.Message);
                }
            }
            Logger.Log.Error($"{errorMessage}");
            return errorMessage.ToString();
        }

        public bool DeleteFile(string fileName, string targetFolder)
        {
            try
            {
                var targetPath = String.Concat(targetFolder, fileName.Replace(@"/", @"\"));
                if (System.IO.File.Exists(targetPath))
                {
                    System.IO.File.Delete($"{targetPath}");
                }
                _currentUser = GetCurrentUser();
                Logger.Log.Info($"{_currentUser.Email} Deleted file {fileName}");
                return true;
            }
            catch (Exception e)
            {
                Logger.Log.Error(e.Message);
                return false;
            }
        }

        public async Task SenEmailAboutAddProcedure(ProcedureViewModel model)
        {
            try
            {
                var prod = _productService.GetProduct((int)model.ProductId);
                if (prod == null)
                {
                    Logger.Log.Error($"{Resources.ErrorMessages.EmailNotSendCantFindProdToProc} ProcedureId={model.Id}");
                    return;
                }

                var product = Mapper.Map<ProductDTO, ShowProductModel>(prod);
                var productName = product.ProductName;
                var userEmailsToNotify = await _countryService.GetCountryUsersEmailsViaNameAsync(product.Country);

                var subject = Resources.Email.SubjectProcedureCreate.Replace("(name)", productName) + $" {model.ProcedureType}" + $" in {product.Country}";
                var body = $"{Resources.Email.BodyProcedureCreate} {model.ProcedureType} for <b>{productName}</b> in {product.Country}<br><br>" +
                           $"<b>Pharmaceutical Form:</b> {product.PharmaceuticalForm}<br><br>" +
                           $"<b>Strength:</b> {product.Strength}<br><br><hr>" +
                           $"<b>Name:</b> {model.Name}<br><br>" +
                           $"<b>SubmissionDate:</b> {model.SubmissionDate}<br><br>" +
                           $"<b>EstimatedApprovalDate:</b> {model.EstimatedApprovalDate}<br><br>" +
                           $"<b>ApprovalDate:</b> {model.ApprovalDate}<br><br>" +
                           $"<b>Comments:</b> {model.Comments}<br><br>" +
                           $"{Resources.Email.Signature}";
                var emailerDto = Mapper.Map<Emailer,EmailerDTO>(emailer);
                if (model.SubmissionDate >= DateTime.Parse("2018-11-11 00:00:00"))
                {
                    await _emailService.SendEmailNotification(body, subject, emailerDto, userEmailsToNotify, toSend);
                }
            }
            catch (Exception ex)
            {
                var userName = User.Identity.Name;
                Logger.Log.Error($"{userName}: SenEmailAboutAddUpdateProduct() {ex.Message} ");
            }
        }
        
       
        public async Task SenEmailAboutUpdateProcedureAsync(ProcedureEditModel model)
        {
            try
            {
                var prod = await _productService.FindAsync((int)model.ProductId);
                if (prod == null)
                {
                    Logger.Log.Error($"{Resources.ErrorMessages.EmailNotSendCantFindProdToProc} ProcedureId={model.Id}");
                    return;
                }

                var product = Mapper.Map<ProductDTO, ShowProductModel>(prod);
                var productName = product.ProductName;
                var userEmailsToNotify = await _countryService.GetCountryUsersEmailsViaNameAsync(product.Country);

                var body = new StringBuilder();

                var subject = Resources.Email.SubjectProcedureUpdate.Replace("(name)", productName) + $" in {product.Country}";
                body.Append(Resources.Email.BodyProcedureUpdate.Replace("(name)", productName) + $" in {product.Country}");
                body.Append($" {model.ProcedureType}");
                var bodyCompared = await CreateBodyText(model);

                if (!string.IsNullOrEmpty(bodyCompared))
                {
                    body.Append(":<br>");
                    body.Append(bodyCompared);
                    body.Append(Resources.Email.Signature);
                    var emailerDto = Mapper.Map<Emailer, EmailerDTO>(emailer);
                    if (model.SubmissionDate >= DateTime.Parse("2018-11-11 00:00:00"))
                    {
                        await _emailService.SendEmailNotification(body.ToString(), subject, emailerDto, userEmailsToNotify, toSend);
                    }
                }
            }
            catch (Exception ex)
            {
                var userName = User.Identity.Name;
                Logger.Log.Error($"{userName}: SenEmailAboutAddUpdateProduct() {ex.Message} ");
            }
        }

        public async Task<string> CreateBodyText(ProcedureEditModel model)
        {
            var bodyStr = new StringBuilder();

            var oldProcedure = await _procedureService.GetItemAsyncWithNoTrack(model.Id);

            if (oldProcedure.EstimatedSubmissionDate == null && model.EstimatedSubmissionDate != null)
            {
                bodyStr.Append($"<strong>{Resources.Labels.EstimatedSubmissionDate}</strong> was added: " +
                               $"{model.EstimatedSubmissionDate.ToString().Substring(0, 10)}<br>");
            }
            else if (oldProcedure.EstimatedSubmissionDate != null && oldProcedure.EstimatedSubmissionDate != model.EstimatedSubmissionDate)
            {
                bodyStr.Append($"<strong>{Resources.Labels.EstimatedSubmissionDate}</strong> changed to: " +
                               $"{model.EstimatedSubmissionDate.ToString().Substring(0, 10)}<br>");
            }

            if (oldProcedure.SubmissionDate == DateTime.MinValue && model.SubmissionDate != DateTime.MinValue)
            {
                bodyStr.Append($"<strong>{Resources.Labels.SubmissionDate}</strong> was added: " +
                               $"{model.SubmissionDate.ToString(CultureInfo.InvariantCulture).Substring(0, 10)}<br>");

            }
            else if (oldProcedure.SubmissionDate != DateTime.MinValue && oldProcedure.SubmissionDate != model.SubmissionDate)
            {
                bodyStr.Append($"<strong>{Resources.Labels.SubmissionDate}</strong> changed to: " +
                               $"{model.SubmissionDate.ToString(CultureInfo.InvariantCulture).Substring(0, 10)}<br>");
            }

            if (oldProcedure.EstimatedApprovalDate == DateTime.MinValue && model.EstimatedApprovalDate != DateTime.MinValue)
            {
                bodyStr.Append($"<strong>{Resources.Labels.EstimatedApprovalDate}</strong> was added: " +
                               $"{model.EstimatedApprovalDate.ToString(CultureInfo.InvariantCulture).Substring(0, 10)}<br>");

            }
            else if (oldProcedure.EstimatedApprovalDate != DateTime.MinValue &&  oldProcedure.EstimatedApprovalDate != model.EstimatedApprovalDate)
            {
                bodyStr.Append($"<strong>{Resources.Labels.EstimatedApprovalDate}</strong> changed to: " +
                               $"{model.EstimatedApprovalDate.ToString(CultureInfo.InvariantCulture).Substring(0, 10)}<br>");
            }


            if (oldProcedure.ApprovalDate == null && model.ApprovalDate != null)
            {
                bodyStr.Append($"<strong>{Resources.Labels.ApprovalDate}</strong> was added: " +
                               $"{model.ApprovalDate.ToString().Substring(0, 10)}<br>");

            }
            else if (oldProcedure.ApprovalDate != null && oldProcedure.ApprovalDate != model.ApprovalDate)
            {
                bodyStr.Append($"<strong>{Resources.Labels.ApprovalDate}</strong> changed to: " +
                               $"{model.ApprovalDate.ToString().Substring(0, 10)}<br>");
            }

            if (oldProcedure.Comments != model.Comments)
            {
                bodyStr.Append($"<strong>Comments</strong> changed to {model.Comments}<br>");
            }

            if (model.ProcedureDocuments != null && model.ProcedureDocuments.Count > 0)
            {
                foreach (var doc in model.ProcedureDocuments)
                {
                    var newDocument = new ProcedureDocument() { PathToDocument = doc.PathToDocument, ProcedureId = doc.ProcedureId, ProcedureDocsType = doc.ProcedureDocsType};
                    var res = oldProcedure.ProcedureDocuments.FirstOrDefault(a => a.PathToDocument == doc.PathToDocument && a.ProcedureId == doc.ProcedureId && a.ProcedureDocsType == doc.ProcedureDocsType);
                    var res2 = oldProcedure.ProcedureDocuments.Contains(newDocument);
                    if (res == null && !res2)
                    {
                        bodyStr.Append($"<strong>To {doc.ProcedureDocsType} added document:</strong> {doc.PathToDocument}<br>");
                    }
                }
            }
            return bodyStr.ToString();
        }

        public async Task SendEmailAboutAddFileProcedure(string procedureId, string productId, ProcedureDocsType procedureDocsType, string localFileName)
        {
            try
            {
                var prod = _productService.GetProduct(int.Parse(productId));
                if (prod == null)
                {
                    Logger.Log.Error($"{Resources.ErrorMessages.EmailNotSendCantFindProdToProc} ProcedureId={procedureId}");
                    return;
                }

                var product = Mapper.Map<ProductDTO, ShowProductModel>(prod);
                var productName = product.ProductName;
                var userEmailsToNotify = await _countryService.GetCountryUsersEmailsViaNameAsync(product.Country);

                var body = new StringBuilder();

                var subject = Resources.Email.SubjectProcedureUpdate.Replace("(name)", productName) + $" in {product.Country}";
                body.Append(Resources.Email.BodyProcedureUpdate.Replace("(name)", productName) + $" in {product.Country}");
                var bodyCompared = $"<strong>To procedure {procedureDocsType} added document:</strong> {localFileName}<br>";
                if (!string.IsNullOrEmpty(bodyCompared))
                {
                    body.Append(":<br>");
                    body.Append(bodyCompared);
                    body.Append(Resources.Email.Signature);
                    var emailerDto = Mapper.Map<Emailer, EmailerDTO>(emailer);
                    var proc = _procedureService.GetItem(int.Parse(procedureId));
                    if (proc.SubmissionDate >= DateTime.Parse("2018-11-11 00:00:00"))
                    {
                        await _emailService.SendEmailNotification(body.ToString(), subject, emailerDto,
                            userEmailsToNotify, toSend);
                    }
                }
            }
            catch (Exception ex)
            {
                var userName = User.Identity.Name;
                Logger.Log.Error($"{userName}: SenEmailAboutAddUpdateProduct() {ex.Message} ");
            }
        }

        public UserViewModel GetCurrentUser()
        {
            try
            {
                if (_currentUser == null)
                {
                    var userId = HttpContext.User.Identity.GetUserId();
                    var user = UserService.GetUser(userId);
                    var userMapper = MapperForUser.GetUserMapperForView(UserService);
                    _currentUser = userMapper.Map<UserDTO, UserViewModel>(user);
                }

                return _currentUser;
            }
            catch (Exception ex)
            {
                return new UserViewModel();
            }
        }

        [HttpPost]
        public string DownloadZip(string filesToDownload, string archName, string productId)
        {
            try
            {
                lock (locker)
                {
                    var urlToDonload = _archProccessor.DownloadZip(filesToDownload, archName, productId,
                        "\\Upload\\Documents\\Procedures\\");
                    return @Url.Content(urlToDonload);
                }
            }
            catch (Exception ex)
            {
                var curDate = DateTime.Now.ToShortDateString().Replace("-", "").Replace(":", "").Replace(".", "").Replace("\\", "");
                Logger.Log.Error($"DownloadZip: curDate={curDate} - {ex.Message}");
                return $"../Procedure/ProductProcedures/{productId}";
            }
        }

        [HttpGet]
        public ActionResult OptimizedProcedures1(int? countryId, string sortOrder, string currentFilter, string searchString, int? page)
       {
            if (User.IsInRole("Holder"))
            {
                return null;
            }
            
            ViewBag.CountryId = countryId;
            ViewBag.User = GetCurrentUser();
            ViewBag.DocsType = Enum.GetValues(typeof(ProcedureDocsType));
            
            if (searchString != null)
            {
                page = 1;
            }
            else
            {
                searchString = currentFilter;
            }

            int pageSize = 5;
            int pageNumber = (page ?? 1);

            @ViewBag.CountryId = countryId;

            ViewBag.CurrentSort = sortOrder;
            ViewBag.ProcedureTypeSortParm = string.IsNullOrEmpty(sortOrder) ? "ProcedureType_desc" : "";
            ViewBag.DateSortParm = sortOrder == "Date" ? "date_desc" : "Date";
            ViewBag.CurrentFilter = searchString;

            var proc = _procedureService.GetPaginated(countryId, searchString, sortOrder, pageNumber, pageSize, out int totalRecords, out int recordsFiltered);
            var procedures = Mapper.Map<IEnumerable<ProcedureDTO>, IEnumerable<ProcedureViewModel>>(proc).ToList();
            var pagedIst = procedures.ToPagedList(pageNumber, pageSize);

            var country = _countryService.GetItem((int)countryId);
            ViewBag.CountryName = country.Name;
            ViewBag.CountryId = country.Id;

            return View("OptimizedProcedures", pagedIst);
        }

        [HttpGet]
        public ActionResult OptimizedProcedures(int? countryId)
        {
            _currentUser = GetCurrentUser();
            /*Todo add to check && !User.IsInRole("Holder")*/
            if (_currentUser.Countries.All(a => a.Id != countryId) && (!User.IsInRole("Admin") || !User.IsInRole("Manager")))
            {
                ViewBag.Error = Resources.ErrorMessages.NoPermission;
                return View("Error");
            }
            ViewBag.User = _currentUser;

            var country = Mapper.Map<CountryDTO, CountryViewModel>(_countryService.GetItem((int)countryId));
            @ViewBag.CountryName = country.Name;
            @ViewBag.CountryId = countryId;
            return View();
        }

        [HttpPost]
        public ActionResult GetOptimizedProcedures(int? countryId)
        {
            if (countryId == 0)
            {
                return null;
            }       

            int start = Convert.ToInt32(Request["start"]);
            int length = Convert.ToInt32(Request["length"]);
            string searchValue = Request["search[value]"];
            string sortColumnName = Request["columns[" + Request["order[0][column]"] + "][name]"];
            string sortDirection = Request["order[0][dir]"];

            var proceduresDto = _procedureService.GetProceduresOptimized(countryId, searchValue, sortColumnName, sortDirection, start, length, out int totalrows, out int totalrowsafterfiltering);

            var config = new MapperConfiguration(cfg => cfg.CreateMap<ProcedureDTO, ProcedureViewOptimized>()
                    .ForMember(dest => dest.ProcedureType, opt => opt.MapFrom(c => c.ProcedureType.ToString()))
                    .ForMember(dest => dest.ProductInfo, opt => opt.MapFrom(c => string.Format($"{c.Product.ProductName.Name} {c.Product.ProductCode}")))
                    .ForMember(dest => dest.Name, opt => opt.MapFrom(c => c.Name))
                    .ForMember(dest => dest.EstimatedSubmissionDate, opt => opt.MapFrom(c => c.EstimatedSubmissionDate.ToString()))
                    .ForMember(dest => dest.SubmissionDate, opt => opt.MapFrom(c => c.SubmissionDate.ToString()))
                    .ForMember(dest => dest.EstimatedApprovalDate, opt => opt.MapFrom(c => c.EstimatedApprovalDate.ToString()))
                    .ForMember(dest => dest.ApprovalDate, opt => opt.MapFrom(c => c.ApprovalDate.ToString()))
                    .ForMember(dest => dest.Comments, opt => opt.MapFrom(c => c.Comments))
                    .ForMember(dest => dest.ProductId, opt => opt.MapFrom(c => c.ProductId))
                    .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id)));
            var mapper = config.CreateMapper();
            var procedures = mapper.Map<List<ProcedureDTO>, List<ProcedureViewOptimized>>(proceduresDto.ToList());

            var json = Json(new { data = procedures, draw = Request["draw"], recordsTotal = totalrows, recordsFiltered = totalrowsafterfiltering }, JsonRequestBehavior.AllowGet);
            return json;
        }
    }
}
