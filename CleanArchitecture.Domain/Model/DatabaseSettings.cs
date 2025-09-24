namespace CleanArchitecture.Domain.Model
{
    public class DatabaseSettings
    {
        public string ConnectionString { get; set; } = null!;

        public string DatabaseName { get; set; } = null!;

        public string PlayersCollectionName { get; set; } = null!;
        public string VerificationCodesCollectionName { get; set; } = null!;

        public string RoomsCollectionName { get; set; } = null!;
    }
}
