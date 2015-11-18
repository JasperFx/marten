var mt_events = require('mt_events');

module.exports = mt_events.transform({
    timing: inline,

    // TODO -- like to capture the Stream here.
    members_joined: function(evt, stream) {
        return {Day: evt.Day, Location: evt.Location, Id: evt.Id, Quest: stream.id}
    },

    members_departed: function (evt, stream) {
        return { Day: evt.Day, Location: evt.Location, Id: evt.Id, Quest: stream.id }
    },
});