using Microsoft.Extensions.DependencyInjection;

namespace System
{
    public static class Extention
    {
        public static void AddDefaultMessages(this IServiceCollection services)
        {
            services.AddAdornerDialogMessage();
            //services.AddWindowDialogMessage();
            services.AddWindowMessage();
            services.AddFormMessageService();
            services.AddNoticeMessage();
            services.AddSnackMessage();
            services.AddIOFolderDialogService();
        }
    }
}
