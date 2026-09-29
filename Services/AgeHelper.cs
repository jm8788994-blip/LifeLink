namespace LifeLink.Services
{
    public static class AgeHelper
    {
        public static int CalculateAge(DateTime birthDate)
        {
            var today = DateTime.UtcNow.Date;
            var age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age))
            {
                age--;
            }
            return age;
        }

        // No DOB on record -> cannot prove the user is a minor, so treat as eligible.
        public static bool IsAdult(DateTime? birthDate)
        {
            return !birthDate.HasValue || CalculateAge(birthDate.Value) >= 18;
        }
    }
}