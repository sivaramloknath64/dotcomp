using System;
using System.Collections.Generic;
using System.Linq;
using SmartStore.Core.Data;
using SmartStore.Core.Domain.Directory;
using SmartStore.Core.Events;
using SmartStore.Data.Caching;

namespace SmartStore.Services.Directory
{
	public partial class StateProvinceService : IStateProvinceService
    {
        private readonly IRepository<StateProvince> _stateProvinceRepository;
        private readonly IEventPublisher _eventPublisher;

        public StateProvinceService(
            IRepository<StateProvince> stateProvinceRepository,
            IEventPublisher eventPublisher)
        {
            _stateProvinceRepository = stateProvinceRepository;
            _eventPublisher = eventPublisher;
        }

        public virtual void DeleteStateProvince(StateProvince stateProvince)
        {
            if (stateProvince == null)
                throw new ArgumentNullException("stateProvince");
            
            _stateProvinceRepository.Delete(stateProvince);
        }

		public virtual IQueryable<StateProvince> GetAllStateProvinces(bool showHidden = false)
		{
			var query = _stateProvinceRepository.Table;

			if (!showHidden)
				query = query.Where(x => x.Published);

			return query;
		}

		public virtual StateProvince GetStateProvinceById(int stateProvinceId)
        {
            if (stateProvinceId == 0)
                return null;

            return _stateProvinceRepository.GetById(stateProvinceId);
        }

        public virtual StateProvince GetStateProvinceByAbbreviation(string abbreviation)
        {
            var query = from sp in _stateProvinceRepository.Table
                        where sp.Abbreviation == abbreviation
                        select sp;
            var stateProvince = query.FirstOrDefault();
            return stateProvince;
        }
        
        /// <summary>
        /// Get teh state filtered based on counryid
        /// </summary>
        /// <param name="countryId"></param>
        /// <returns></returns>
        public virtual IList<StateProvince> GetStateProvincesByCountryId(int countryId)
        {
            if (countryId == 0)
            {
                return new List<StateProvince>();
            }
			var query = from sp in _stateProvinceRepository.Table
						where sp.CountryId == countryId &&
						 sp.Published
						select sp;

			return query.ToList();
		}

        public virtual void InsertStateProvince(StateProvince stateProvince)
        {
            if (stateProvince == null)
                throw new ArgumentNullException("stateProvince");

            _stateProvinceRepository.Insert(stateProvince);
        }

        public virtual void UpdateStateProvince(StateProvince stateProvince)
        {
            if (stateProvince == null)
                throw new ArgumentNullException("stateProvince");

            _stateProvinceRepository.Update(stateProvince);
        }
    }
}
