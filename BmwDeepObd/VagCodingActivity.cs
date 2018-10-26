﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Text;
using Android.Content;
using Android.Content.Res;
using Android.Hardware.Usb;
using Android.OS;
using Android.Support.V7.App;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using EdiabasLib;

namespace BmwDeepObd
{
    [Android.App.Activity(
        WindowSoftInputMode = SoftInput.StateAlwaysHidden,
        ConfigurationChanges = Android.Content.PM.ConfigChanges.KeyboardHidden |
                               Android.Content.PM.ConfigChanges.Orientation |
                               Android.Content.PM.ConfigChanges.ScreenSize)]
    public class VagCodingActivity : AppCompatActivity, View.IOnTouchListener
    {
        public class InstanceData
        {
            public int SelectedSubsystem { get; set; }
            public byte[] CurrentCoding { get; set; }
            public string CurrentDataFileName { get; set; }
        }

        // Intent extra
        public const string ExtraEcuName = "ecu_name";
        public const string ExtraEcuDir = "ecu_dir";
        public const string ExtraTraceDir = "trace_dir";
        public const string ExtraTraceAppend = "trace_append";
        public const string ExtraInterface = "interface";
        public const string ExtraDeviceAddress = "device_address";
        public const string ExtraEnetIp = "enet_ip";

        public static XmlToolActivity.EcuInfo IntentEcuInfo { get; set; }

        private InstanceData _instanceData = new InstanceData();
        private InputMethodManager _imm;
        private View _contentView;
        private LinearLayout _layoutVagCoding;
        private TextView _textViewVagCodingSubsystem;
        private Spinner _spinnerVagCodingSubsystem;
        private StringObjAdapter _spinnerVagCodingSubsystemAdapter;
        private TextView _textViewVagCodingRaw;
        private EditText _editTextVagCodingRaw;
        private LinearLayout _layoutVagCodingAssitant;
        private ResultListAdapter _layoutVagCodingAssitantAdapter;
        private ListView _listViewVagCodingAssistant;
        private ActivityCommon _activityCommon;
        private XmlToolActivity.EcuInfo _ecuInfo;
        private EdiabasNet _ediabas;
        private Thread _jobThread;
        private bool _activityActive;
        private bool _ediabasJobAbort;
        private string _ecuDir;
        private string _traceDir;
        private bool _traceAppend;
        private string _deviceAddress;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            if (savedInstanceState != null)
            {
                _instanceData = ActivityCommon.GetInstanceState(savedInstanceState, _instanceData) as InstanceData;
            }

            SupportActionBar.SetHomeButtonEnabled(true);
            SupportActionBar.SetDisplayShowHomeEnabled(true);
            SupportActionBar.SetDisplayHomeAsUpEnabled(true);
            //SupportActionBar.SetDisplayShowCustomEnabled(true);
            SupportActionBar.Title = string.Format(GetString(Resource.String.vag_coding_title), Intent.GetStringExtra(ExtraEcuName) ?? string.Empty);
            SetContentView(Resource.Layout.vag_coding);

            _imm = (InputMethodManager)GetSystemService(InputMethodService);
            _contentView = FindViewById<View>(Android.Resource.Id.Content);

            SetResult(Android.App.Result.Canceled);

            _activityCommon = new ActivityCommon(this, () =>
            {

            }, BroadcastReceived);

            _ecuDir = Intent.GetStringExtra(ExtraEcuDir);
            _traceDir = Intent.GetStringExtra(ExtraTraceDir);
            _traceAppend = Intent.GetBooleanExtra(ExtraTraceAppend, true);
            _activityCommon.SelectedInterface = (ActivityCommon.InterfaceType)
                Intent.GetIntExtra(ExtraInterface, (int)ActivityCommon.InterfaceType.None);
            _deviceAddress = Intent.GetStringExtra(ExtraDeviceAddress);
            _activityCommon.SelectedEnetIp = Intent.GetStringExtra(ExtraEnetIp);

            _ecuInfo = IntentEcuInfo;

            _layoutVagCoding = FindViewById<LinearLayout>(Resource.Id.layoutVagCoding);
            _layoutVagCoding.SetOnTouchListener(this);

            _textViewVagCodingSubsystem = FindViewById<TextView>(Resource.Id.textViewVagCodingSubsystem);
            _textViewVagCodingSubsystem.SetOnTouchListener(this);

            _spinnerVagCodingSubsystem = FindViewById<Spinner>(Resource.Id.spinnerVagCodingSubsystem);
            _spinnerVagCodingSubsystem.SetOnTouchListener(this);
            _spinnerVagCodingSubsystemAdapter = new StringObjAdapter(this);
            _spinnerVagCodingSubsystem.Adapter = _spinnerVagCodingSubsystemAdapter;
            _spinnerVagCodingSubsystem.ItemSelected += SubSystemItemSelected;

            _textViewVagCodingRaw = FindViewById<TextView>(Resource.Id.textViewVagCodingRaw);
            _textViewVagCodingRaw.SetOnTouchListener(this);

            _editTextVagCodingRaw = FindViewById<EditText>(Resource.Id.editTextVagCodingRaw);
            _editTextVagCodingRaw.SetOnTouchListener(this);

            _layoutVagCodingAssitant = FindViewById<LinearLayout>(Resource.Id.layoutVagCodingAssitant);
            _textViewVagCodingRaw.SetOnTouchListener(this);

            _listViewVagCodingAssistant = FindViewById<ListView>(Resource.Id.listViewVagCodingAssistant);
            _layoutVagCodingAssitantAdapter = new ResultListAdapter(this, -1, 0, true);
            _listViewVagCodingAssistant.Adapter = _layoutVagCodingAssitantAdapter;
            _listViewVagCodingAssistant.SetOnTouchListener(this);

            UpdateCodingSubsystemList();
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            ActivityCommon.StoreInstanceState(outState, _instanceData);
            base.OnSaveInstanceState(outState);
        }

        protected override void OnStart()
        {
            base.OnStart();
            if (_activityCommon.MtcBtService)
            {
                _activityCommon.StartMtcService();
            }
            _activityCommon.RequestUsbPermission(null);
        }

        protected override void OnResume()
        {
            base.OnResume();
            _activityActive = true;
        }

        protected override void OnPause()
        {
            base.OnPause();
            _activityActive = false;
        }

        protected override void OnStop()
        {
            base.OnStop();
            if (_activityCommon.MtcBtService)
            {
                _activityCommon.StopMtcService();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _ediabasJobAbort = true;
            if (IsJobRunning())
            {
                _jobThread.Join();
            }
            EdiabasClose();
            _activityCommon.Dispose();
            _activityCommon = null;
        }

        public override void OnBackPressed()
        {
            StoreResults();
            base.OnBackPressed();
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            HideKeyboard();
            switch (item.ItemId)
            {
                case Android.Resource.Id.Home:
                    StoreResults();
                    Finish();
                    return true;
            }
            return base.OnOptionsItemSelected(item);
        }

        public bool OnTouch(View v, MotionEvent e)
        {
            switch (e.Action)
            {
                case MotionEventActions.Down:
                    HideKeyboard();
                    break;
            }
            return false;
        }

        private void EdiabasOpen()
        {
            if (_ediabas == null)
            {
                _ediabas = new EdiabasNet
                {
                    EdInterfaceClass = _activityCommon.GetEdiabasInterfaceClass(),
                    AbortJobFunc = AbortEdiabasJob
                };
                _ediabas.SetConfigProperty("EcuPath", _ecuDir);
                if (!string.IsNullOrEmpty(_traceDir))
                {
                    _ediabas.SetConfigProperty("TracePath", _traceDir);
                    _ediabas.SetConfigProperty("IfhTrace", string.Format("{0}", (int)EdiabasNet.EdLogLevel.Error));
                    _ediabas.SetConfigProperty("AppendTrace", _traceAppend ? "1" : "0");
                    _ediabas.SetConfigProperty("CompressTrace", "1");
                }
                else
                {
                    _ediabas.SetConfigProperty("IfhTrace", "0");
                }
            }

            _activityCommon.SetEdiabasInterface(_ediabas, _deviceAddress);
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private bool EdiabasClose()
        {
            if (IsJobRunning())
            {
                return false;
            }
            if (_ediabas != null)
            {
                _ediabas.Dispose();
                _ediabas = null;
            }
            return true;
        }

        private bool IsJobRunning()
        {
            if (_jobThread == null)
            {
                return false;
            }
            if (_jobThread.IsAlive)
            {
                return true;
            }
            _jobThread = null;
            return false;
        }

        private bool AbortEdiabasJob()
        {
            if (_ediabasJobAbort)
            {
                return true;
            }
            return false;
        }

        private void BroadcastReceived(Context context, Intent intent)
        {
            if (intent == null)
            {   // from usb check timer
                if (_activityActive)
                {
                    _activityCommon.RequestUsbPermission(null);
                }
                return;
            }
            string action = intent.Action;
            switch (action)
            {
                case UsbManager.ActionUsbDeviceAttached:
                    if (_activityActive)
                    {
                        if (intent.GetParcelableExtra(UsbManager.ExtraDevice) is UsbDevice usbDevice)
                        {
                            _activityCommon.RequestUsbPermission(usbDevice);
                        }
                    }
                    break;

                case UsbManager.ActionUsbDeviceDetached:
                    if (_activityCommon.SelectedInterface == ActivityCommon.InterfaceType.Ftdi)
                    {
                        if (intent.GetParcelableExtra(UsbManager.ExtraDevice) is UsbDevice usbDevice &&
                            EdFtdiInterface.IsValidUsbDevice(usbDevice))
                        {
                            EdiabasClose();
                        }
                    }
                    break;
            }
        }

        private void StoreResults()
        {
        }

        private void SubSystemItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            HideKeyboard();
            UpdateCoding();
        }

        private void UpdateCodingSubsystemList()
        {
            int selection = _instanceData.SelectedSubsystem;

            _spinnerVagCodingSubsystemAdapter.Items.Clear();
            if (_ecuInfo.VagCodingShort != null || _ecuInfo.VagCodingLong != null)
            {
                int index = 0;
                StringBuilder sb = new StringBuilder();
                sb.Append(string.Format("{0}: ", index));
                bool append = false;
                if (!string.IsNullOrEmpty(_ecuInfo.VagPartNumber))
                {
                    sb.Append(_ecuInfo.VagPartNumber);
                    append = true;
                }
                if (!string.IsNullOrEmpty(_ecuInfo.VagSysName))
                {
                    if (append)
                    {
                        sb.Append(" / ");
                    }
                    sb.Append(_ecuInfo.VagSysName);
                }
                _spinnerVagCodingSubsystemAdapter.Items.Add(new StringObjType(sb.ToString(), index));
            }

            if (_ecuInfo.SubSystems != null)
            {
                foreach (XmlToolActivity.EcuInfoSubSys subSystem in _ecuInfo.SubSystems)
                {
                    if (subSystem.VagCodingLong != null)
                    {
                        int index = subSystem.SubSysIndex + 1;
                        StringBuilder sb = new StringBuilder();
                        sb.Append(string.Format("{0}: ", index));
                        bool append = false;
                        if (!string.IsNullOrEmpty(subSystem.VagPartNumber))
                        {
                            sb.Append(subSystem.VagPartNumber);
                            append = true;
                        }
                        if (!string.IsNullOrEmpty(subSystem.Name))
                        {
                            if (append)
                            {
                                sb.Append(" / ");
                            }
                            sb.Append(subSystem.Name);
                        }
                        _spinnerVagCodingSubsystemAdapter.Items.Add(new StringObjType(sb.ToString(), index));
                    }
                }
            }
            _spinnerVagCodingSubsystemAdapter.NotifyDataSetChanged();

            _spinnerVagCodingSubsystem.SetSelection(selection);
            UpdateCoding();
        }

        private void UpdateCoding()
        {
            _instanceData.CurrentCoding = null;
            _instanceData.CurrentDataFileName = null;
            if (_spinnerVagCodingSubsystem.SelectedItemPosition >= 0)
            {
                int subSystemIndex = (int)_spinnerVagCodingSubsystemAdapter.Items[_spinnerVagCodingSubsystem.SelectedItemPosition].Data;
                _instanceData.SelectedSubsystem = subSystemIndex;

                string dataFileName = null;
                byte[] coding = null;
                if (subSystemIndex == 0)
                {
                    if (_ecuInfo.VagCodingLong != null)
                    {
                        coding = _ecuInfo.VagCodingLong;
                    }
                    else if (_ecuInfo.VagCodingShort != null)
                    {
                        coding = BitConverter.GetBytes(_ecuInfo.VagCodingShort.Value);
                    }
                    dataFileName = _ecuInfo.VagDataFileName;
                }
                else
                {
                    if (_ecuInfo.SubSystems != null)
                    {
                        foreach (XmlToolActivity.EcuInfoSubSys subSystem in _ecuInfo.SubSystems)
                        {
                            if (subSystem.SubSysIndex + 1 == subSystemIndex)
                            {
                                coding = subSystem.VagCodingLong;
                                dataFileName = subSystem.VagDataFileName;
                                break;
                            }
                        }
                    }
                }

                if (coding != null)
                {
                    _instanceData.CurrentCoding = new byte[coding.Length];
                    Array.Copy(coding, _instanceData.CurrentCoding, coding.Length);
                }

                _instanceData.CurrentDataFileName = dataFileName;

                string codingText = string.Empty;
                if (coding != null)
                {
                    codingText = BitConverter.ToString(coding).Replace("-", " ");
                }

                _editTextVagCodingRaw.Text = codingText;
            }

            UpdateCodingAssistant();
        }

        private void UpdateCodingAssistant()
        {
            _layoutVagCodingAssitantAdapter.Items.Clear();
            if (_instanceData.CurrentCoding != null && _instanceData.CurrentDataFileName != null)
            {
                UdsFileReader.UdsReader udsReader = ActivityCommon.GetUdsReader(_instanceData.CurrentDataFileName);
                // ReSharper disable once UseNullPropagation
                if (udsReader != null)
                {
                    List<UdsFileReader.DataReader.DataInfo> dataInfoList =
                        udsReader.DataReader.ExtractDataType(_instanceData.CurrentDataFileName, UdsFileReader.DataReader.DataType.LongCoding);
                    if (dataInfoList != null)
                    {
                        StringBuilder sbComment = new StringBuilder();
                        foreach (UdsFileReader.DataReader.DataInfo dataInfo in dataInfoList)
                        {
                            if (dataInfo is UdsFileReader.DataReader.DataInfoLongCoding dataInfoLongCoding)
                            {
                                if (!string.IsNullOrEmpty(dataInfoLongCoding.Text))
                                {
                                    bool textLine = dataInfoLongCoding.LineNumber != null;
                                    if (textLine)
                                    {
                                        if (sbComment.Length > 0)
                                        {
                                            sbComment.Append("\r\n");
                                        }
                                        sbComment.Append(dataInfoLongCoding.Text);
                                    }
                                    else
                                    {
                                        if (sbComment.Length > 0)
                                        {
                                            _layoutVagCodingAssitantAdapter.Items.Add(new TableResultItem(sbComment.ToString(), null, null, false, false));
                                            sbComment.Clear();
                                        }

                                        if (dataInfoLongCoding.Byte != null)
                                        {
                                            StringBuilder sb = new StringBuilder();
                                            sb.Append(string.Format("{0}: ", dataInfoLongCoding.Byte.Value));
                                            if (dataInfoLongCoding.Bit != null)
                                            {
                                                sb.Append(string.Format("Bit {0}", dataInfoLongCoding.Bit.Value));
                                            }
                                            else if (dataInfoLongCoding.BitMin != null && dataInfoLongCoding.BitMax != null && dataInfoLongCoding.BitValue != null)
                                            {
                                                sb.Append(string.Format("Bit {0}-{1}={2:X02}",
                                                    dataInfoLongCoding.BitMin.Value, dataInfoLongCoding.BitMax.Value, dataInfoLongCoding.BitValue.Value));
                                            }

                                            sb.Append(" ");
                                            sb.Append(dataInfoLongCoding.Text);
                                            _layoutVagCodingAssitantAdapter.Items.Add(new TableResultItem(sb.ToString(), null, dataInfoLongCoding, true, false));
                                        }
                                    }
                                }
                            }
                        }
                        if (sbComment.Length > 0)
                        {
                            _layoutVagCodingAssitantAdapter.Items.Add(new TableResultItem(sbComment.ToString(), null, null, false, false));
                            sbComment.Clear();
                        }
                    }
                }
            }
            _layoutVagCodingAssitantAdapter.NotifyDataSetChanged();
            _layoutVagCodingAssitant.Visibility = _layoutVagCodingAssitantAdapter.Items.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
        }

        private void HideKeyboard()
        {
            _imm?.HideSoftInputFromWindow(_contentView.WindowToken, HideSoftInputFlags.None);
        }
    }
}