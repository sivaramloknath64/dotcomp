﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using Newtonsoft.Json;
using SmartStore.Admin.Models.Rules;
using SmartStore.ComponentModel;
using SmartStore.Core.Data;
using SmartStore.Core.Domain.Common;
using SmartStore.Core.Logging;
using SmartStore.Core.Security;
using SmartStore.Rules;
using SmartStore.Rules.Domain;
using SmartStore.Rules.Filters;
using SmartStore.Services.Cart.Rules;
using SmartStore.Services.Customers;
using SmartStore.Services.Localization;
using SmartStore.Services.Payments;
using SmartStore.Web.Framework.Controllers;
using SmartStore.Web.Framework.Filters;
using SmartStore.Web.Framework.Plugins;
using SmartStore.Web.Framework.Security;
using Telerik.Web.Mvc;

namespace SmartStore.Admin.Controllers
{
    [AdminAuthorize]
    public partial class RuleController : AdminControllerBase
    {
        private readonly IRuleFactory _ruleFactory;
        private readonly IRuleStorage _ruleStorage;
        private readonly ITargetGroupService _targetGroupService;
        private readonly IRuleTemplateSelector _ruleTemplateSelector;
        private readonly Func<RuleScope, IRuleProvider> _ruleProvider;
        private readonly IEnumerable<IRuleOptionsProvider> _ruleOptionsProviders;
        private readonly Lazy<IPaymentService> _paymentService;
        private readonly Lazy<PluginMediator> _pluginMediator;
        private readonly AdminAreaSettings _adminAreaSettings;

        public RuleController(
            IRuleFactory ruleFactory,
            IRuleStorage ruleStorage,
            ITargetGroupService targetGroupService,
            IRuleTemplateSelector ruleTemplateSelector,
            Func<RuleScope, IRuleProvider> ruleProvider,
            IEnumerable<IRuleOptionsProvider> ruleOptionsProviders,
            Lazy<IPaymentService> paymentService,
            Lazy<PluginMediator> pluginMediator,
            AdminAreaSettings adminAreaSettings)
        {
            _ruleFactory = ruleFactory;
            _ruleStorage = ruleStorage;
            _targetGroupService = targetGroupService;
            _ruleTemplateSelector = ruleTemplateSelector;
            _ruleProvider = ruleProvider;
            _ruleOptionsProviders = ruleOptionsProviders;
            _paymentService = paymentService;
            _pluginMediator = pluginMediator;
            _adminAreaSettings = adminAreaSettings;
        }

        // Ajax.
        public ActionResult AllRuleSets(string selectedIds, RuleScope? scope)
        {
            var ruleSets = _ruleStorage.GetAllRuleSets(false, false, scope, includeHidden: true);
            var selectedArr = selectedIds.ToIntArray();

            ruleSets.Add(new RuleSetEntity { Id = -1, Name = T("Admin.Rules.AddRule").Text + "…" });

            var data = ruleSets
                .Select(x => new
                {
                    id = x.Id.ToString(),
                    text = x.Name,
                    selected = selectedArr.Contains(x.Id),
                    urlTitle = x.Id == -1 ? string.Empty : T("Admin.Rules.OpenRule").Text,
                    url = x.Id == -1
                        ? Url.Action("Create", "Rule", new { area = "admin" })
                        : Url.Action("Edit", "Rule", new { id = x.Id, area = "admin" })
                })
                .ToList();

            return new JsonResult { Data = data, JsonRequestBehavior = JsonRequestBehavior.AllowGet };
        }

        public ActionResult Index()
        {
            return RedirectToAction("List");
        }

        [Permission(Permissions.System.Rule.Read)]
        public ActionResult List()
        {
            var model = new RuleSetListModel
            {
                GridPageSize = _adminAreaSettings.GridPageSize
            };

            return View(model);
        }

        [HttpPost, GridAction(EnableCustomBinding = true)]
        [Permission(Permissions.System.Rule.Read)]
        public ActionResult List(GridCommand command)
        {
            var gridModel = new GridModel<RuleSetModel>();
            var ruleSets = _ruleStorage.GetAllRuleSets(false, false, null, command.Page - 1, command.PageSize, false, true);

            gridModel.Data = ruleSets.Select(x =>
            {
                var rsModel = MiniMapper.Map<RuleSetEntity, RuleSetModel>(x);
                rsModel.ScopeName = x.Scope.GetLocalizedEnum(Services.Localization, Services.WorkContext);
                return rsModel;
            });

            gridModel.Total = ruleSets.TotalCount;

            return new JsonResult
            {
                Data = gridModel
            };
        }

        [Permission(Permissions.System.Rule.Create)]
        public ActionResult Create()
        {
            var model = new RuleSetModel();

            PrepareTemplateViewBag(0);

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        [ValidateInput(false)]
        [Permission(Permissions.System.Rule.Create)]
        public ActionResult Create(RuleSetModel model, bool continueEditing)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var ruleSet = MiniMapper.Map<RuleSetModel, RuleSetEntity>(model);

            _ruleStorage.InsertRuleSet(ruleSet);

            NotifySuccess(T("Admin.Rules.RuleSet.Added"));

            return continueEditing ? RedirectToAction("Edit", new { id = ruleSet.Id }) : RedirectToAction("List");
        }

        [Permission(Permissions.System.Rule.Read)]
        public ActionResult Edit(int id /* ruleSetId */)
        {
            var entity = _ruleStorage.GetRuleSetById(id, false, true);
            if (entity == null || entity.IsSubGroup)
            {
                return HttpNotFound();
            }

            var model = MiniMapper.Map<RuleSetEntity, RuleSetModel>(entity);
            model.ScopeName = entity.Scope.GetLocalizedEnum(Services.Localization, Services.WorkContext);

            var provider = _ruleProvider(entity.Scope);
            model.ExpressionGroup = _ruleFactory.CreateExpressionGroup(entity, provider, true);
            model.AvailableDescriptors = _targetGroupService.RuleDescriptors;

            model.AssignedToDiscounts = entity.Discounts
                .Select(x => new RuleSetModel.AssignedToEntityModel { Id = x.Id, Name = x.Name.NullEmpty() ?? x.Id.ToString() })
                .ToList();

            model.AssignedToShippingMethods = entity.ShippingMethods
                .Select(x => new RuleSetModel.AssignedToEntityModel { Id = x.Id, Name = x.GetLocalized(y => y.Name) })
                .ToList();

            var paymentMethods = entity.PaymentMethods;
            if (paymentMethods.Any())
            {
                var paymentProviders = _paymentService.Value.LoadAllPaymentMethods().ToDictionarySafe(x => x.Metadata.SystemName);

                model.AssignedToPaymentMethods = paymentMethods
                    .Select(x =>
                    {
                        string friendlyName = null;
                        if (paymentProviders.TryGetValue(x.PaymentMethodSystemName, out var paymentProvider))
                        {
                            friendlyName = _pluginMediator.Value.GetLocalizedFriendlyName(paymentProvider.Metadata);
                        }

                        return new RuleSetModel.AssignedToEntityModel
                        {
                            Id = x.Id,
                            Name = friendlyName.NullEmpty() ?? x.PaymentMethodSystemName,
                            SystemName = x.PaymentMethodSystemName
                        };
                    })
                    .ToList();
            }

            PrepareExpressions(model.ExpressionGroup);
            PrepareTemplateViewBag(entity.Id);

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        [Permission(Permissions.System.Rule.Update)]
        public ActionResult Edit(RuleSetModel model, bool continueEditing)
        {
            var ruleSet = _ruleStorage.GetRuleSetById(model.Id, true, true);

            MiniMapper.Map(model, ruleSet);

            _ruleStorage.UpdateRuleSet(ruleSet);

            if (model.RawRuleData.HasValue())
            {
                try
                {
                    var ruleData = JsonConvert.DeserializeObject<RuleEditItem[]>(model.RawRuleData);

                    SaveRuleData(ruleData, model.Scope);
                }
                catch (Exception ex)
                {
                    NotifyError(ex);
                }
            }

            return continueEditing ? RedirectToAction("Edit", new { id = ruleSet.Id }) : RedirectToAction("List");
        }

        [HttpPost, ActionName("Delete")]
        [Permission(Permissions.System.Rule.Delete)]
        public ActionResult DeleteConfirmed(int id)
        {
            var ruleSet = _ruleStorage.GetRuleSetById(id, false, false);
            if (ruleSet == null)
            {
                return HttpNotFound();
            }

            _ruleStorage.DeleteRuleSet(ruleSet);

            NotifySuccess(T("Admin.Rules.RuleSet.Deleted"));
            return RedirectToAction("List");
        }


        [HttpPost]
        [Permission(Permissions.System.Rule.Create)]
        public ActionResult AddRule(int ruleSetId, RuleScope scope, string ruleType)
        {
            var provider = _ruleProvider(scope);
            var descriptor = provider.RuleDescriptors.FindDescriptor(ruleType);
            var op = descriptor.Operators.First();

            var rule = new RuleEntity
            {
                RuleSetId = ruleSetId,
                RuleType = ruleType,
                Operator = op.Operator
            };

            if (op == RuleOperator.In || op == RuleOperator.NotIn)
            {
                // Avoid ArgumentException "The 'In' operator only supports non-null instances from types that implement 'ICollection<T>'."
                rule.Value = string.Empty;
            }

            _ruleStorage.InsertRule(rule);

            var expression = provider.VisitRule(rule);

            PrepareTemplateViewBag(ruleSetId);

            return PartialView("_Rule", expression);
        }

        [HttpPost]
        [Permission(Permissions.System.Rule.Update)]
        public ActionResult UpdateRules(RuleEditItem[] ruleData, RuleScope ruleScope)
        {
            try
            {
                SaveRuleData(ruleData, ruleScope);
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, ex.Message });
            }

            return Json(new { Success = true });
        }

        [HttpPost]
        [Permission(Permissions.System.Rule.Delete)]
        public ActionResult DeleteRule(int ruleId)
        {
            var rule = _ruleStorage.GetRuleById(ruleId, true);
            if (rule == null)
            {
                NotifyError(T("Admin.Rules.NotFound", ruleId));
                return Json(new { Success = false });
            }

            _ruleStorage.DeleteRule(rule);

            return Json(new { Success = true });
        }

        [HttpPost]
        [Permission(Permissions.System.Rule.Update)]
        public ActionResult ChangeOperator(int ruleSetId, string op)
        {
            var ruleSet = _ruleStorage.GetRuleSetById(ruleSetId, false, false);

            ruleSet.LogicalOperator = op.IsCaseInsensitiveEqual("and") ? LogicalRuleOperator.And : LogicalRuleOperator.Or;

            _ruleStorage.UpdateRuleSet(ruleSet);

            return Json(new { Success = true });
        }

        [HttpPost]
        [Permission(Permissions.System.Rule.Create)]
        public ActionResult AddGroup(int ruleSetId, RuleScope scope)
        {
            var provider = _ruleProvider(scope);

            var group = new RuleSetEntity
            {
                IsActive = true,
                IsSubGroup = true,
                Scope = scope
            };
            _ruleStorage.InsertRuleSet(group);

            var groupRefRule = new RuleEntity
            {
                RuleSetId = ruleSetId,
                RuleType = "Group",
                Operator = RuleOperator.IsEqualTo,
                Value = group.Id.ToString()
            };
            _ruleStorage.InsertRule(groupRefRule);

            var expression = provider.VisitRuleSet(group);
            expression.RefRuleId = groupRefRule.Id;

            return PartialView("_RuleSet", expression);
        }

        [HttpPost]
        [Permission(Permissions.System.Rule.Delete)]
        public ActionResult DeleteGroup(int refRuleId)
        {
            var refRule = _ruleStorage.GetRuleById(refRuleId, true);
            var ruleSetId = refRule?.Value?.ToInt() ?? 0;

            var group = _ruleStorage.GetRuleSetById(ruleSetId, true, false);
            if (group == null)
            {
                NotifyError(T("Admin.Rules.GroupNotFound", ruleSetId));
                return Json(new { Success = false });
            }

            _ruleStorage.DeleteRule(refRule);
            _ruleStorage.DeleteRuleSet(group);

            return Json(new { Success = true });
        }

        [HttpPost]
        [Permission(Permissions.System.Rule.Execute)]
        public ActionResult Execute(int ruleSetId)
        {
            var success = true;
            var message = string.Empty;

            try
            {
                var entity = _ruleStorage.GetRuleSetById(ruleSetId, false, true);

                switch (entity.Scope)
                {
                    case RuleScope.Customer:
                        {
                            var expression = _ruleFactory.CreateExpressionGroup(entity, _targetGroupService, true) as FilterExpression;
                            var result = _targetGroupService.ProcessFilter(new[] { expression }, LogicalRuleOperator.And, 0, 500);

                            message = T("Admin.Rules.Execute.MatchCustomers", result.TotalCount.ToString("N0"));
                        }
                        break;
                    case RuleScope.Cart:
                        {
                            var customer = Services.WorkContext.CurrentCustomer;
                            var provider = _ruleProvider(entity.Scope) as ICartRuleProvider;
                            var expression = _ruleFactory.CreateExpressionGroup(entity, provider, true) as RuleExpression;

                            var match = provider.RuleMatches(new[] { expression }, LogicalRuleOperator.And);

                            message = T(match ? "Admin.Rules.Execute.MatchCart" : "Admin.Rules.Execute.DoesNotMatchCart", customer.Username.NullEmpty() ?? customer.Email);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                success = false;
                message = ex.Message;
                Logger.Error(ex);
            }

            return Json(new
            {
                Success = success,
                Message = message.NaIfEmpty()
            });
        }

        // Ajax.
        public ActionResult RuleOptions(int ruleId, int rootRuleSetId, string term, int? page)
        {
            var rule = _ruleStorage.GetRuleById(ruleId, false);
            if (rule == null)
            {
                throw new SmartException(T("Admin.Rules.NotFound", ruleId));
            }

            var provider = _ruleProvider(rule.RuleSet.Scope);
            var expression = provider.VisitRule(rule);

            Func<RuleValueSelectListOption, bool> optionsPredicate = x => true;
            RuleOptionsResult options = null;

            if (expression.Descriptor.SelectList is RemoteRuleValueSelectList list)
            {
                var optionsProvider = _ruleOptionsProviders.FirstOrDefault(x => x.Matches(list.DataSource));
                if (optionsProvider != null)
                {
                    options = optionsProvider.GetOptions(RuleOptionsRequestReason.SelectListOptions, expression, page ?? 0, 100, term);
                    if (list.DataSource == "CartRule" || list.DataSource == "TargetGroup")
                    {
                        optionsPredicate = x => x.Value != rootRuleSetId.ToString();
                    }
                }
            }

            if (options == null)
            {
                options = new RuleOptionsResult();
            }

            var data = options.Options
                .Where(optionsPredicate)
                .Select(x => new RuleSelectItem { Id = x.Value, Text = x.Text, Hint = x.Hint })
                .ToList();

            // Mark selected items.
            var selectedValues = expression.RawValue.SplitSafe(",");
            data.Each(x => x.Selected = selectedValues.Contains(x.Id));

            return new JsonResult
            {
                Data = new
                {
                    hasMoreData = options.HasMoreData,
                    results = data
                },
                MaxJsonLength = int.MaxValue,
                JsonRequestBehavior = JsonRequestBehavior.AllowGet
            };
        }

        private void PrepareExpressions(IRuleExpressionGroup group)
        {
            if (group == null)
            {
                return;
            }

            foreach (var expression in group.Expressions)
            {
                if (!expression.Descriptor.IsValid)
                {
                    expression.Metadata["Error"] = T("Admin.Rules.InvalidDescriptor").Text;
                }

                if (expression is IRuleExpressionGroup subGroup)
                {
                    PrepareExpressions(subGroup);
                    continue;
                }

                // Load name and subtitle (e.g. SKU) for selected options.
                if (expression.Descriptor.SelectList is RemoteRuleValueSelectList list)
                {
                    var optionsProvider = _ruleOptionsProviders.FirstOrDefault(x => x.Matches(list.DataSource));
                    if (optionsProvider != null)
                    {
                        var options = optionsProvider.GetOptions(RuleOptionsRequestReason.SelectedDisplayNames, expression, 0, int.MaxValue, null);

                        expression.Metadata["SelectedItems"] = options.Options.ToDictionarySafe(
                            x => x.Value,
                            x => new RuleSelectItem { Text = x.Text, Hint = x.Hint });
                    }
                }
            }
        }

        private void PrepareTemplateViewBag(int rootRuleSetId)
        {
            ViewBag.RootRuleSetId = rootRuleSetId;
            ViewBag.TemplateSelector = _ruleTemplateSelector;
            //ViewBag.LanguageSeoCode = Services.WorkContext.WorkingLanguage.UniqueSeoCode.EmptyNull().ToLower();
        }

        private void SaveRuleData(RuleEditItem[] ruleData, RuleScope ruleScope)
        {
            var rules = _ruleStorage.GetRulesByIds(ruleData?.Select(x => x.RuleId)?.ToArray(), true);
            if (!rules.Any())
            {
                return;
            }

            using (var scope = new DbContextScope(ctx: Services.DbContext, autoCommit: false))
            {
                var rulesDic = rules.ToDictionarySafe(x => x.Id);
                var provider = _ruleProvider(ruleScope);

                foreach (var data in ruleData)
                {
                    if (rulesDic.TryGetValue(data.RuleId, out var entity))
                    {
                        // TODO? Ugly. There should be a better way. Do not store culture variant values.
                        if (data.Value.HasValue())
                        {
                            var descriptor = provider.RuleDescriptors.FindDescriptor(entity.RuleType);

                            if (descriptor.RuleType == RuleType.Money)
                            {
                                data.Value = data.Value.Convert<decimal>(CultureInfo.CurrentCulture).ToString(CultureInfo.InvariantCulture);
                            }
                            else if (descriptor.RuleType == RuleType.Float || descriptor.RuleType == RuleType.NullableFloat)
                            {
                                data.Value = data.Value.Convert<float>(CultureInfo.CurrentCulture).ToString(CultureInfo.InvariantCulture);
                            }
                            else if (descriptor.RuleType == RuleType.DateTime || descriptor.RuleType == RuleType.NullableDateTime)
                            {
                                data.Value = data.Value.Convert<DateTime>(CultureInfo.CurrentCulture).ToString(CultureInfo.InvariantCulture);
                            }
                        }
                        //if (data.Value?.Contains(',') ?? false)
                        //{
                        //    var provider = _ruleProvider(ruleScope);
                        //    var descriptor = provider.RuleDescriptors.FindDescriptor(entity.RuleType);
                        //    var floatingPointTypes = new Type[] { typeof(decimal), typeof(decimal?), typeof(float), typeof(float?), typeof(double), typeof(double?) };

                        //    if (floatingPointTypes.Contains(descriptor.RuleType.ClrType))
                        //    {
                        //        data.Value = data.Value.Replace(",", ".");
                        //    }
                        //}

                        entity.Operator = data.Op;
                        entity.Value = data.Value;

                        _ruleStorage.UpdateRule(entity);
                    }
                }

                scope.Commit();
            }
        }
    }
}