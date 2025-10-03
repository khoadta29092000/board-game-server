using CleanArchitecture.Domain.Model.Splendor.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.IRepository
{
    public interface ISplendorRepository
    {
        Task<List<CardEntity>> LoadCardsAsync();
        Task<List<NobleEntity>> LoadNoblesAsync();
    }
}
