using CleanArchitecture.Domain.Model.Player;
using HotChocolate.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.GraphQL
{
    public class PlayerType : ObjectType<Player>
    {
    }
}
