namespace NewGaza
{
    public sealed partial class CityHud
    {
        private bool legacyProjectPage;
        public void OpenLegacyProjects()
        {
            legacyProjectPage = true;
            OpenPage(Page.Projects);
        }
    }
}