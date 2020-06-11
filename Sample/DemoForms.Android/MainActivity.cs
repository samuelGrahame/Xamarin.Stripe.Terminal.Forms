﻿using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.OS;
using FreshMvvm;
using Acr.UserDialogs;
using DemoForms.Droid.Services;
using Com.Stripe.Stripeterminal;
using Android.Support.V4.Content;
using Android.Support.V4.App;
using Xamarin.Stripe.Terminal.Forms;

namespace DemoForms.Droid
{
    [Activity(Label = "DemoForms", Icon = "@mipmap/icon", Theme = "@style/MainTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        private TerminalLifecycleObserver _observer = TerminalLifecycleObserver.Instance;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            TabLayoutResource = Resource.Layout.Tabbar;
            ToolbarResource = Resource.Layout.Toolbar;

            base.OnCreate(savedInstanceState);

            // Register the IStripeTerminalService dependency
            FreshIOC.Container.Register<IStripeTerminalService, TerminalService>();

            UserDialogs.Init(this);

            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);
            LoadApplication(new App());

            if (ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.AccessFineLocation) != Permission.Granted)
            {
                ActivityCompat.RequestPermissions(this, new[] { Android.Manifest.Permission.AccessFineLocation }, 10);
            }
            else
            {
                DoTerminalSetup();
            }

            // Register Stripe Callback
            Application.RegisterActivityLifecycleCallbacks(_observer);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            for (int i = 0; i < permissions.Length - 1; i++)
            {
                var permission = permissions[i];
                var grant = grantResults[i];

                if (permission == Android.Manifest.Permission.AccessFineLocation && grant == Permission.Granted)
                {
                    DoTerminalSetup();
                }
            }
        }

        private void DoTerminalSetup()
        {
            var terminalService = FreshIOC.Container.Resolve<IStripeTerminalService>() as TerminalService;
            var tokenProvider = new TokenProvider();
            StripeTerminal.InitTerminal(Application.Context, tokenProvider, terminalService);
            terminalService.SafeInitialize();
            terminalService.InitTerminalManager();
        }
    }
}