var mt_transforms = require('mt_transforms');
mt_transforms.transform({
    timing: 'inline',
    name: 'location',

    // TODO -- like to capture the Stream here.
    members_joined: function(evt, stream) {
        return {Day: evt.Day, Location: evt.Location, Id: evt.Id, Quest: stream.id}
    },

    members_departed: function (evt, stream) {
        return { Day: evt.Day, Location: evt.Location, Id: evt.Id, Quest: stream.id }
    }
});
