using System;
using System.Linq;
using System.Web.Mvc;
using SmartStore.Core;
using SmartStore.Core.Caching;
using SmartStore.Services.Directory;
using SmartStore.Services.Localization;
using SmartStore.Web.Infrastructure.Cache;
using SmartStore.Web.Framework.Controllers;

namespace SmartStore.Web.Controllers
{
    public partial class CountryController : PublicControllerBase
    {
        #region Fields

        private readonly ICountryService _countryService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;
        private readonly ICacheManager _cacheManager;

        #endregion

        #region Constructors

        public CountryController(ICountryService countryService,
            IStateProvinceService stateProvinceService,
            ILocalizationService localizationService,
            IWorkContext workContext,
            ICacheManager cacheManager)
        {
            this._countryService = countryService;
            this._stateProvinceService = stateProvinceService;
            this._localizationService = localizationService;
            this._workContext = workContext;
            this._cacheManager = cacheManager;
        }

        #endregion

        #region States / provinces

        [AcceptVerbs(HttpVerbs.Get)]
        public ActionResult GetAllStates()
        {

            var states = _stateProvinceService.GetAllStateProvinces().ToList();
            var result = (from s in states
                          select new { id = s.Id, name = s.GetLocalized(x => x.Name).Value })
                          .ToList();
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// It will fetch states based on country id. If the country id will not have any state 
        /// mapped to it, the method will will reture"Other as a value"
        /// </summary>
        /// <param name="countryId"></param>
        /// <returns></returns>
        [AcceptVerbs(HttpVerbs.Get)]
        public ActionResult GetStatesByCountryId(int countryId)
        {
            var states = _stateProvinceService.GetStateProvincesByCountryId(countryId).ToList();

            var result = (from s in states
                          select new { id = s.Id, name = s.Name })
                          .ToList();

            if (result.Count == 0)
            {
                result.Insert(0, new { id = 0, name = _localizationService.GetResource("Address.Other") });
            }

            return Json(result, JsonRequestBehavior.AllowGet);
        }

        #endregion
    }
}
