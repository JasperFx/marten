function getCurrentLocation() {
    try {
        navigator.geolocation.getCurrentPosition(showCurrentLocation, errorHandler)
    }
    catch (e) {
        errorHandler();
    }
}

function errorHandler() {
    NerdDinner.getCurrentLocationByIpAddress();
}

function showCurrentLocation(position) {
    NerdDinner.getCurrentLocationByLatLong(position.coords.latitude, position.coords.longitude);
}
