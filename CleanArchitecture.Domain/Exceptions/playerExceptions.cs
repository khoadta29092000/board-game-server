using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Exceptions
{
    public class InvalidCredentialsException : Exception
    {
        public InvalidCredentialsException(string msg) : base(msg) { }
    }

    public class NotVerifiedException : Exception
    {
        public NotVerifiedException(string msg) : base(msg) { }
    }

    public class NotActiveException : Exception
    {
        public NotActiveException(string msg) : base(msg) { }
    }
}
