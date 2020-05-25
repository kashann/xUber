using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Acr.UserDialogs;
using TrackingSample.Helpers;
using UberClone.Helpers;
using UberClone.Models;
using UberClone.Services;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.GoogleMaps;

namespace UberClone.ViewModels
{
    public class MapPageViewModel : INotifyPropertyChanged
    {
        IGoogleMapsApiService googleMapsApi = new GoogleMapsApiService();
        public PageStatusEnum PageStatusEnum { get; set; }
        public ICommand DrawRouteCommand { get; set; }
        public ICommand GetPlacesCommand { get; set; }
        public ICommand GetUserLocationCommand { get; set; }
        public ICommand ChangePageStatusCommand { get; set; }
        public ICommand GetLocationNameCommand { get; set; }
        public ICommand CenterMapCommand { get; set; }
        public ICommand GetPlaceDetailCommand { get; set; }
        public ICommand LoadRouteCommand { get; set; }
        public ICommand CleanPolylineCommand { get; set; }
        public ICommand ChooseLocationCommand { get; set; }

        public ObservableCollection<GooglePlaceAutoCompletePrediction> Places { get; set; }
        public ObservableCollection<GooglePlaceAutoCompletePrediction> RecentPlaces { get; set; }
        public GooglePlaceAutoCompletePrediction RecentPlace1 { get; set; }
        public GooglePlaceAutoCompletePrediction RecentPlace2 { get; set; }
        public ObservableCollection<PriceOption> PriceOptions { get; set; }
        public PriceOption PriceOptionSelected { get; set; }

        public string Hello { get; set; }

        int _currentIndex;
        public int CurrentIndex
        {
            get => _currentIndex;
            set
            {
                _currentIndex = value;
                PriceOptionSelected = PriceOptions[CurrentIndex];
            }
        }

        public string PickupLocation { get; set; }

        Location OriginCoordinates { get; set; }
        Location DestinationCoordinates { get; set; }

        string _destinationLocation;
        public string DestinationLocation
        {
            get => _destinationLocation;
            set
            {
                _destinationLocation = value;
                if (!string.IsNullOrEmpty(_destinationLocation))
                {
                   GetPlacesCommand.Execute(_destinationLocation);
                }
            }
        }

        GooglePlaceAutoCompletePrediction _placeSelected;
        public GooglePlaceAutoCompletePrediction PlaceSelected
        {
            get => _placeSelected;
            set
            {
                _placeSelected = value;
                if (_placeSelected != null)
                    GetPlaceDetailCommand.Execute(_placeSelected);
            }
        }

        public MapPageViewModel()
        {
            LoadRouteCommand = new Command(async () => await LoadRoute());
            GetPlaceDetailCommand = new Command<GooglePlaceAutoCompletePrediction>(async (param) => await GetPlacesDetail(param));
            GetPlacesCommand = new Command<string>(async (param) => await GetPlacesByName(param));
            GetUserLocationCommand = new Command(async () => await GetActualUserLocation());
            GetLocationNameCommand = new Command<Position>(async (param) => await GetLocationName(param));
            ChangePageStatusCommand = new Command<PageStatusEnum>((param) =>
              {
                 PageStatusEnum = param;

                  if (PageStatusEnum == PageStatusEnum.Default)
                  {
                      CleanPolylineCommand.Execute(null);
                      GetUserLocationCommand.Execute(null);
                      DestinationLocation = string.Empty;
                  } else if (PageStatusEnum == PageStatusEnum.Searching)
                  {
                      Places = new ObservableCollection<GooglePlaceAutoCompletePrediction>(RecentPlaces);
                  }
              });

            ChooseLocationCommand = new Command<Position>((param) =>
            {
                if(PageStatusEnum == PageStatusEnum.Searching)
                {
                    GetLocationNameCommand.Execute(param);
                }
            });

            DateTime now = DateTime.Now;
            if (now.Hour < 13)
                Hello = "Good morning, ";
            else if (now.Hour >= 13 && now.Hour < 17)
                Hello = "Good afternoon, ";
            else if (now.Hour >= 17 && now.Hour < 24)
                Hello = "Good evening, ";
            Hello += User.ShortName;

            FillRecentPlacesList();
            FillPriceOptions();
            GetUserLocationCommand.Execute(null);
        }

        async Task GetActualUserLocation()
        {
            try
            {
                await Task.Yield();
                var request = new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(5000));
                var location = await Geolocation.GetLocationAsync(request);

                if (location != null)
                {
                    OriginCoordinates = location;
                    CenterMapCommand.Execute(location);
                    GetLocationNameCommand.Execute(new Position(location.Latitude, location.Longitude));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                await UserDialogs.Instance.AlertAsync("Error", "Unable to get actual location", "Ok");
            }
        }

        //Get place 
        public async Task GetLocationName(Position position)
        {
            try
            {
                var placemarks = await Geocoding.GetPlacemarksAsync(position.Latitude, position.Longitude);
                PickupLocation = placemarks?.FirstOrDefault()?.Thoroughfare;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        public async Task GetPlacesByName(string placeText)
        {
            var places = await googleMapsApi.GetPlaces(placeText);
            var placeResult = places.AutoCompletePlaces;
            if (placeResult != null && placeResult.Count > 0)
            {
                Places = new ObservableCollection<GooglePlaceAutoCompletePrediction>(placeResult);
            }
        }

        public async Task GetPlacesDetail(GooglePlaceAutoCompletePrediction placeA)
        {
            var place = await googleMapsApi.GetPlaceDetails(placeA.PlaceId);
            if (place != null)
            {
                DestinationCoordinates = new Location(place.Latitude, place.Longitude);
                LoadRouteCommand.Execute(null);
                RecentPlaces.Add(placeA);
            }
        }

        public async Task LoadRoute()
        {
            if (OriginCoordinates == null)
                return;

            ChangePageStatusCommand.Execute(PageStatusEnum.ShowingRoute);

            var googleDirection = await googleMapsApi.GetDirections($"{OriginCoordinates.Latitude}", $"{OriginCoordinates.Longitude}", $"{DestinationCoordinates.Latitude}", $"{DestinationCoordinates.Longitude}");
            if (googleDirection.Routes != null && googleDirection.Routes.Count > 0)
            {
                var positions = (Enumerable.ToList(PolylineHelper.Decode(googleDirection.Routes.First().OverviewPolyline.Points)));
                DrawRouteCommand.Execute(positions);
            }
            else
            {
                ChangePageStatusCommand.Execute(PageStatusEnum.Default);
                await UserDialogs.Instance.AlertAsync(":(", "No route found", "Ok");
            }
        }

        void FillRecentPlacesList()
        {
            RecentPlaces = new ObservableCollection<GooglePlaceAutoCompletePrediction>()
            {
                {new GooglePlaceAutoCompletePrediction(){ PlaceId="ChIJjQ4mfFL_sUAREGCHYsdsNQU", StructuredFormatting=new StructuredFormatting(){ MainText="Academia de Studii Economice din București", SecondaryText="Piața Romană 6" } } },
                {new GooglePlaceAutoCompletePrediction(){ PlaceId="ChIJs64X4oH4sUAR88atRxH-ww8", StructuredFormatting=new StructuredFormatting(){ MainText="Pipera Metro Station", SecondaryText="Bulevardul Dimitrie Pompeiu" } } },
                {new GooglePlaceAutoCompletePrediction(){ PlaceId="ChIJMQNHJu0BskARxnQnYcO9P2A", StructuredFormatting=new StructuredFormatting(){ MainText="Complex Studențesc Belvedere ASE", SecondaryText="Strada Chibzuinței 2" } } },
            };

            RecentPlace1 = RecentPlaces[0];
            RecentPlace2 = RecentPlaces[1];

        }

        void FillPriceOptions()
        {
            PriceOptions = new ObservableCollection<PriceOption>()
            {
                { new PriceOption() { Tag = "PanneX", Category = "Panne X", CategoryDescription = "Affortable, everyday towing", 
                    PriceDetails = new System.Collections.Generic.List<PriceDetail>() {
                        { new PriceDetail() { Type = "On-Site repair", Price = 150, ArrivalETA = EtaPlus(15), Icon = "panner.png" } },
                        { new PriceDetail() { Type = "Normal", Price = 180, ArrivalETA = EtaPlus(25), Icon = "pannex.jpg" } }
                    }
                } },
                { new PriceOption() { Tag = "PanneXL", Category = "Panne XL", CategoryDescription = "Affortable towing for big trucks",
                    PriceDetails = new System.Collections.Generic.List<PriceDetail>() {
                        { new PriceDetail() { Type = "Truck", Price = 240, ArrivalETA = EtaPlus(35), Icon = "pannex.jpg" } }
                    } 
                } },
                { new PriceOption() { Tag = "PanneS", Category = "Panne S", CategoryDescription = "Towing low cars", 
                    PriceDetails = new System.Collections.Generic.List<PriceDetail>() {
                        { new PriceDetail() { Type = "Sports", Price = 280, ArrivalETA = EtaPlus(30), Icon = "pannes.jpg" } },
                        { new PriceDetail() { Type = "Exotic", Price = 400, ArrivalETA = EtaPlus(45), Icon = "pannes.jpg" } }
                    }
                } }
            };
            PriceOptionSelected = PriceOptions[CurrentIndex];

        }
        public event PropertyChangedEventHandler PropertyChanged;

        private string EtaPlus(int minutes)
        {
            DateTime time = DateTime.Now.AddMinutes(minutes);
            if(time.Hour > 9 && time.Minute > 9)
                return time.Hour + ":" + time.Minute;
            else if(time.Hour <= 9 && time.Minute <= 9)
                return "0" + time.Hour + ":0" + time.Minute;
            else if(time.Hour <= 9 && time.Minute > 9)
                return "0" + time.Hour + ":" + time.Minute;
            else
                return time.Hour + ":0" + time.Minute;
        }
    }
}
