function NerdDinner() { }

NerdDinner.MapDivId = 'theMap';
NerdDinner._map = null;
NerdDinner._points = [];
NerdDinner._shapes = [];
NerdDinner.ipInfoDbKey = '';
NerdDinner.BingMapsKey = '';

NerdDinner.LoadMap = function (latitude, longitude, onMapLoaded) {
    NerdDinner._map = new VEMap(NerdDinner.MapDivId);

    var options = new VEMapOptions();

    options.EnableBirdseye = false

    // Makes the control bar less obtrusize.
    this._map.SetDashboardSize(VEDashboardSize.Small);

    if (onMapLoaded != null)
        NerdDinner._map.onLoadMap = onMapLoaded;

    if (latitude != null && longitude != null) {
        var center = new VELatLong(latitude, longitude);
    }

    NerdDinner._map.LoadMap(center, null, null, null, null, null, null, options);
}

NerdDinner.ClearMap = function () {
    if (NerdDinner._map != null) {
        NerdDinner._map.Clear();
    }
    NerdDinner._points = [];
    NerdDinner._shapes = [];
}

NerdDinner.LoadPin = function (LL, name, description, draggable) {
    if (LL.Latitude == 0 || LL.Longitude == 0) {
        return;
    }

    var shape = new VEShape(VEShapeType.Pushpin, LL);

    if (draggable == true) {
        shape.Draggable = true;
        shape.onenddrag = NerdDinner.onEndDrag;
    }

    //Make a nice Pushpin shape with a title and description
    shape.SetTitle("<span class=\"pinTitle\"> " + escape(name) + "</span>");

    if (description !== undefined) {
        shape.SetDescription("<p class=\"pinDetails\">" + escape(description) + "</p>");
    }

    NerdDinner._map.AddShape(shape);
    NerdDinner._points.push(LL);
    NerdDinner._shapes.push(shape);
}

NerdDinner.FindAddressOnMap = function (where) {
    var numberOfResults = 1;
    var setBestMapView = true;
    var showResults = true;
    var defaultDisambiguation = true;

    NerdDinner._map.Find("", where, null, null, null,
                         numberOfResults, showResults, true, defaultDisambiguation,
                         setBestMapView, NerdDinner._callbackForLocation);
}

NerdDinner._callbackForLocation = function (layer, resultsArray, places, hasMore, VEErrorMessage) {
    NerdDinner.ClearMap();

    if (places == null) {
        NerdDinner._map.ShowMessage(VEErrorMessage);
        return;
    }

    //Make a pushpin for each place we find
    $.each(places, function (i, item) {
        var description = "";
        if (item.Description !== undefined) {
            description = item.Description;
        }
        var LL = new VELatLong(item.LatLong.Latitude,
                        item.LatLong.Longitude);

        NerdDinner.LoadPin(LL, item.Name, description, true);
    });

    //Make sure all pushpins are visible
    if (NerdDinner._points.length > 1) {
        NerdDinner._map.SetMapView(NerdDinner._points);
    }

    //If we've found exactly one place, that's our address.
    //lat/long precision was getting lost here with toLocaleString, changed to toString
    if (NerdDinner._points.length === 1) {
        $("#Latitude").val(NerdDinner._points[0].Latitude.toString());
        $("#Longitude").val(NerdDinner._points[0].Longitude.toString());
    }
}

NerdDinner.FindDinnersGivenLocation = function (where) {
    NerdDinner._map.Find("", where, null, null, null, null, null, false,
                         null, null, NerdDinner._callbackUpdateMapDinners);
}


NerdDinner.FindMostPopularDinners = function (limit) {
    $.post("/Search/GetMostPopularDinners", { "limit": limit }, NerdDinner._renderDinners, "json");
}

NerdDinner._callbackUpdateMapDinners = function (layer, resultsArray, places, hasMore, VEErrorMessage) {
    var center = NerdDinner._map.GetCenter();

    $.post("/Search/SearchByLocation",
           { latitude: center.Latitude, longitude: center.Longitude },
           NerdDinner._renderDinners,
           "json");
}


NerdDinner._renderDinners = function (dinners) {
    $("#dinnerList").empty();

    NerdDinner.ClearMap();

    $.each(dinners, function (i, dinner) {

        var LL = new VELatLong(dinner.Latitude, dinner.Longitude, 0, null);

        // Add Pin to Map
        NerdDinner.LoadPin(LL, _getDinnerLinkHTML(dinner), _getDinnerDescriptionHTML(dinner), false);

        //Add a dinner to the <ul> dinnerList on the right
        $('#dinnerList').append($('<li/>')
                        .attr("class", "dinnerItem")
                        .append(_getDinnerLinkHTML(dinner))
                        .append($('<br/>'))
                        .append(_getDinnerDate(dinner, "mmm d"))
                        .append(" with " + _getRSVPMessage(dinner.RSVPCount)));
    });

    // Adjust zoom to display all the pins we just added.
    if (NerdDinner._points.length > 1) {
        NerdDinner._map.SetMapView(NerdDinner._points);
    }

    // Display the event's pin-bubble on hover.
    $(".dinnerItem").each(function (i, dinner) {
        $(dinner).hover(
            function () { NerdDinner._map.ShowInfoBox(NerdDinner._shapes[i]); },
            function () { NerdDinner._map.HideInfoBox(NerdDinner._shapes[i]); }
        );
    });

    function _getDinnerDate(dinner, formatStr) {
        return '<strong>' + _dateDeserialize(dinner.EventDate).format(formatStr) + '</strong>';
    }

    function _getDinnerLinkHTML(dinner) {
        return '<a href="' + dinner.Url + '">' + dinner.Title + '</a>';
    }

    function _getDinnerDescriptionHTML(dinner) {
        return '<p>' + _getDinnerDate(dinner, "mmmm d, yyyy") + '</p><p>' + dinner.Description + '</p>' + _getRSVPMessage(dinner.RSVPCount);
    }

    function _dateDeserialize(dateStr) {
        return eval('new' + dateStr.replace(/\//g, ' '));
    }


    function _getRSVPMessage(RSVPCount) {
        var rsvpMessage = "" + RSVPCount + " RSVP";

        if (RSVPCount > 1)
            rsvpMessage += "s";

        return rsvpMessage;
    }
}

NerdDinner.onEndDrag = function (e) {
    $("#Latitude").val(e.LatLong.Latitude.toString());
    $("#Longitude").val(e.LatLong.Longitude.toString());
}

NerdDinner.getLocationResults = function (locations) {
    if (locations) {
        var currentAddress = $("#Address");
        if (locations[0].Name != currentAddress) {
            var answer = confirm("Bing Maps returned the address '" + locations[0].Name + "' for the pin location. Click 'OK' to use this address for the event, or 'Cancel' to use the current address of '" + currentAddress.val() + "'");
            if (answer) {
                currentAddress.val(locations[0].Name);
            }
        }
    }
}

NerdDinner.getCurrentLocationByIpAddress = function () {
    var requestUrl = "http://api.ipinfodb.com/v3/ip-city/?format=json&callback=?&key=" + this.ipInfoDbKey;

    $.getJSON(requestUrl,
        function (data) {
            if (data.RegionName != '') {
                $('#Location').val(data.regionName + ', ' + data.countryName);
            }
        });
}

NerdDinner.getCurrentLocationByLatLong = function (latitude, longitude) {
    var requestUrl = 'http://dev.virtualearth.net/REST/v1/Locations/' + latitude + ',' + longitude + '?key=' + NerdDinner.BingMapsKey + '&jsonp=?';
    $.getJSON(requestUrl,
        function (result) {
            if (result.resourceSets[0].estimatedTotal > 0) {
                $('#Location').val(result.resourceSets[0].resources[0].address.formattedAddress)
            }
        });
}
