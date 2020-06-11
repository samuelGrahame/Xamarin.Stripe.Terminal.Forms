﻿using System;
using System.Collections.Generic;
using System.Linq;
using Android.Bluetooth;
using Com.Stripe.Stripeterminal;
using Com.Stripe.Stripeterminal.Callable;
using Com.Stripe.Stripeterminal.Model.External;
using Java.Lang;
using Xamarin.Stripe.Terminal.Forms;

namespace DemoForms.Droid
{
    public class TerminalService : Java.Lang.Object, ITerminalListener, IDiscoveryListener, IStripeTerminalService, IReaderDisplayListener
    {
        private Cancelable _cancelable = null;
        private Cancelable _discoveryCancelable = null;
        private List<Reader> _discoveredReaders = new List<Reader>();
        private Action<IList<StripeTerminalReader>> _onReadersDiscoveredAction;
        private Action<string> _readerMessageNotificationHandler;
        private Action<string> _readerConnectionNotificationHandler;
        private Reader _connectedReader;
        private bool _isInitialized = false;
        private ReaderSoftwareUpdate _softwareUpdate;

        public bool IsTerminalConnected => _isInitialized == false ? false : StripeTerminal.Instance.ConnectionStatus == ConnectionStatus.Connected;

        public void OnUnexpectedReaderDisconnect(Reader reader)
        {
            ConnectionNotifyHandler("Reader Disconnected");
        }

        #region ITerminalListener

        public void OnConnectionStatusChange(ConnectionStatus p0)
        {
            System.Diagnostics.Debug.WriteLine($"*** OnConnectionStatusChange {p0} ***");

            //ConnectionNotifyHandler(p0.ToString());
        }

        public void OnPaymentStatusChange(PaymentStatus p0)
        {
            System.Diagnostics.Debug.WriteLine($"*** OnPaymentStatusChange {p0} ***");
        }

        public void OnReportLowBatteryWarning()
        {

        }

        public void OnReportReaderEvent(ReaderEvent p0)
        {
            System.Diagnostics.Debug.WriteLine($"*** OnReportReaderEvent {p0} ***");
        }

        #endregion

        #region IReaderDisplayListener 

        public void OnRequestReaderDisplayMessage(ReaderDisplayMessage p0)
        {
            System.Diagnostics.Debug.WriteLine("OnRequestReaderDisplayMessage = " + p0.ToString());
            ReaderNotifyHandler(p0.ToString());
        }

        public void OnRequestReaderInput(ReaderInputOptions p0)
        {
            System.Diagnostics.Debug.WriteLine("OnRequestReaderInput = " + p0.ToString());
            ReaderNotifyHandler(p0.ToString());
        }

        #endregion

        public void OnUpdateDiscoveredReaders(IList<Reader> readers)
        {
            if (readers.Any())
            {
                var stripeReaders = new List<StripeTerminalReader>();

                foreach (var reader in readers)
                {
                    stripeReaders.Add(new StripeTerminalReader
                    {
                        IsSimulated = reader.IsSimulated,
                        SerialNumber = reader.SerialNumber,
                        SoftwareVersion = reader.SoftwareVersion
                    });
                }

                _discoveredReaders.AddRange(readers);

                // Report Back
                _onReadersDiscoveredAction(stripeReaders);

                _discoveryCancelable = null;
            }
        }

        #region IStripeTerminalService

        public string ArePermissionsGranted()
        {
            var failedPermissions = string.Empty;

            return failedPermissions;
        }

        public void DiscoverReaders(StripeDiscoveryConfiguration config, Action<IList<StripeTerminalReader>> readers, Action scanTimeoutCallback)
        {
            _onReadersDiscoveredAction = readers;
            _discoveryCancelable?.Cancel(new GenericCallback((ex) =>
            {
                // Do Nothing...
            }));
            _discoveryCancelable = StripeTerminal.Instance.DiscoverReaders(new DiscoveryConfiguration(config.TimeOut, DeviceType.Chipper2x, isSimulated: config.IsSimulated), this, new GenericCallback((ex) =>
            {
                // Do Nothing...
                scanTimeoutCallback();
            }));
        }

        public void CancelDiscover()
        {
            _discoveryCancelable?.Cancel(new GenericCallback((ex) =>
            {
                // Do Nothing...
            }));
            _discoveryCancelable = null;
            _discoveredReaders.Clear();
        }

        public void ConnectToReader(StripeTerminalReader reader, Action<ReaderConnectionResult> onReaderConnectionSuccess)
        {
            var discoveredReader = _discoveredReaders.FirstOrDefault(x => x.SerialNumber == reader.SerialNumber);

            if (discoveredReader != null)
            {
                _connectedReader = discoveredReader;

                StripeTerminal.Instance.ConnectReader(discoveredReader, new ConnectionCallback((reader, e) =>
                {
                    var result = new ReaderConnectionResult
                    {
                        ErrorMessage = e?.ErrorMessage,
                        IsConnected = e == null
                    };

                    System.Diagnostics.Debug.WriteLine(e?.Message + " " + e?.ErrorMessage);
                    onReaderConnectionSuccess?.Invoke(result);
                }));
            }
        }

        public void ReconnectToReader(Action<bool> onReaderConnectionSuccess)
        {
            if (_connectedReader != null)
            {
                StripeTerminal.Instance.ConnectReader(_connectedReader, new ConnectionCallback((reader, e) =>
                {
                    if (e != null)
                        onReaderConnectionSuccess(false);
                    else
                        onReaderConnectionSuccess(true);
                }));
            }
        }

        /// <summary>
        /// Collect payment details
        /// </summary>
        public void RetreivePaymentIntent(string clientPaymentSecret, Action<string> onSuccess, Action<string> onFailure)
        {
            StripeTerminal.Instance.RetrievePaymentIntent(clientPaymentSecret, new PaymentIntentCallback((newIntent, retrievePaymentIntentException) =>
            {
                if (retrievePaymentIntentException != null)
                {
                    onFailure(retrievePaymentIntentException.ErrorMessage);
                }
                else
                {
                    _cancelable = StripeTerminal.Instance.CollectPaymentMethod(newIntent, this, new PaymentIntentCallback((authorizedIntent, collectPaymentMethodException) =>
                    {
                        if (collectPaymentMethodException != null)
                        {
                            _cancelable = null;
                            onFailure(collectPaymentMethodException.ErrorMessage);
                        }
                        else
                        {
                            ReaderNotifyHandler("Authorizing Payment");

                            StripeTerminal.Instance.ProcessPayment(authorizedIntent, new PaymentIntentCallback((pendingCaptureIntent, processPaymentException) =>
                            {
                                if (processPaymentException != null)
                                {
                                    _cancelable = null;
                                    onFailure(processPaymentException.ErrorMessage);
                                }
                                else
                                {
                                    if (pendingCaptureIntent != null && string.IsNullOrEmpty(pendingCaptureIntent.PaymentMethodId) == false && pendingCaptureIntent.Status == PaymentIntentStatus.RequiresCapture)
                                    {
                                        _cancelable = null;
                                        onSuccess(pendingCaptureIntent.PaymentMethodId);
                                    }
                                    else
                                    {
                                        var errorMessage = string.Empty;
                                        if (pendingCaptureIntent == null)
                                        {
                                            errorMessage = "No payment intent was found";
                                        }
                                        else
                                        {
                                            errorMessage = "You payment is in an invalid state try the checkout process again: " + pendingCaptureIntent.Status.ToString();
                                        }

                                        _cancelable = null;
                                        onFailure(errorMessage);
                                    }
                                }
                            }));
                        }
                    }));
                }
            }));
        }

        public void RegisterReaderMessageNotifications(Action<string> notificationHandler)
        {
            _readerMessageNotificationHandler = notificationHandler;
        }

        public void TearDownReaderMessageNotifications()
        {
            _readerMessageNotificationHandler = null;
        }

        public void RegisterConnectionMessageNotifications(Action<string> readerConnectionNotificationHandler)
        {
            _readerConnectionNotificationHandler = readerConnectionNotificationHandler;
        }

        public void TearDownConnectionMessageNotifications()
        {
            _readerConnectionNotificationHandler = null;
        }

        public void CancelPayment()
        {
            if (_cancelable != null)
            {
                System.Diagnostics.Debug.WriteLine("Cancel Payment");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Cancel Payment Fail");
            }

            if (_cancelable != null)
            {
                _cancelable.Cancel(new GenericCallback((ex) =>
                {
                    if (ex != null)
                    {
                        System.Diagnostics.Debug.WriteLine("GenericCallback Cancel Payment Fail");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("GenericCallback Cancel Payment Success");
                    }
                }));
            }
        }

        public void DisconnectReader()
        {
            StripeTerminal.Instance.DisconnectReader(new GenericCallback((ex) =>
            {

            }));
        }

        public void CheckForSoftwareUpdate(Action<string, string> hasUpdate)
        {
            StripeTerminal.Instance.CheckForUpdate(new ReaderSoftwareUpdateCallback((readerUpdate, ex) =>
            {
                if (ex != null)
                {
                    hasUpdate(null, ex.ErrorMessage);
                }
                else if (readerUpdate != null && (readerUpdate.HasFirmwareUpdate || readerUpdate.HasKeyUpdate || readerUpdate.HasConfigUpdate))
                {
                    _softwareUpdate = readerUpdate;
                    hasUpdate(readerUpdate.TimeEstimate.Description, null);
                }

                hasUpdate(null, null);
            }));
        }

        public void UpdateSoftware(Action<float> updateMessage, Action<string> complete)
        {
            StripeTerminal.Instance.InstallUpdate(_softwareUpdate, new ReaderSoftwareUpdateListener((progress) =>
            {
                // Progress
                updateMessage(progress);
            })
            , new GenericCallback((ex) =>
            {
                _softwareUpdate = null;
                complete(ex?.ErrorMessage);
            }));
        }

        #endregion

        private void ReaderNotifyHandler(string message)
        {
            _readerMessageNotificationHandler?.Invoke(message);
        }


        private void ConnectionNotifyHandler(string message)
        {
            _readerConnectionNotificationHandler?.Invoke(message);
        }

        public void SafeInitialize()
        {
            try
            {
                var term = StripeTerminal.Instance;
                _isInitialized = true;
            }
            catch (IllegalStateException)
            {
                Initialize();
            }
        }

        private void Initialize()
        {
            try
            {
                _isInitialized = true;
            }
            catch (TerminalException e)
            {
                throw new RuntimeException("Location services are required in order to initialize the Terminal.", e);
            }
        }

        public void InitTerminalManager()
        {
            if (BluetoothAdapter.DefaultAdapter != null && !BluetoothAdapter.DefaultAdapter.IsEnabled)
            {
                BluetoothAdapter.DefaultAdapter.Enable();
            }

            SafeInitialize();
        }
    }

    public class ConnectionCallback : Java.Lang.Object, IReaderCallback
    {
        private Action<Reader, TerminalException> _callback;

        public ConnectionCallback(Action<Reader, TerminalException> callback)
        {
            _callback = callback;
        }

        public void OnSuccess(Reader reader)
        {
            _callback(reader, null);
        }

        public void OnFailure(TerminalException e)
        {
            _callback(null, e);
        }
    }

    public class PaymentIntentCallback : Java.Lang.Object, IPaymentIntentCallback
    {
        // Screen Callbacks
        private Action<PaymentIntent, TerminalException> _callback;

        public PaymentIntentCallback(Action<PaymentIntent, TerminalException> callback)
        {
            _callback = callback;
        }

        public void OnSuccess(PaymentIntent paymentIntent)
        {
            _callback(paymentIntent, null);
        }

        public void OnFailure(TerminalException e)
        {
            _callback(null, e);
        }
    }

    public class ReaderSoftwareUpdateCallback : Java.Lang.Object, IReaderSoftwareUpdateCallback
    {
        // Screen Callbacks
        private Action<ReaderSoftwareUpdate, TerminalException> _callback;

        public ReaderSoftwareUpdateCallback(Action<ReaderSoftwareUpdate, TerminalException> callback)
        {
            _callback = callback;
        }

        public void OnSuccess(ReaderSoftwareUpdate readerUpdate)
        {
            _callback(readerUpdate, null);
        }

        public void OnFailure(TerminalException e)
        {
            _callback(null, e);
        }
    }

    public class ReaderSoftwareUpdateListener : Java.Lang.Object, IReaderSoftwareUpdateListener
    {
        private Action<float> _callback;

        public ReaderSoftwareUpdateListener(Action<float> callback)
        {
            _callback = callback;
        }

        void IReaderSoftwareUpdateListener.OnReportReaderSoftwareUpdateProgress(float p0)
        {
            _callback(p0);
        }
    }

    public class GenericCallback : Java.Lang.Object, ICallback
    {
        private Action<TerminalException> _callback;

        public GenericCallback(Action<TerminalException> callback)
        {
            _callback = callback;
        }

        public void OnSuccess()
        {
            _callback(null);
        }

        public void OnFailure(TerminalException e)
        {
            _callback(e);
        }

    }
}
