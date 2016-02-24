var mt_transforms = require('mt_transforms');
mt_transforms.transform({
    timing: 'inline',
    name: 'location',

    // TODO -- like to capture the EventStream here.
    members_joined: function(evt, metadata) {
        return {Day: evt.Day, Location: evt.Location, Id: metadata.id, Quest: metadata.stream}
    },

    members_departed: function (evt, metadata) {
        return { Day: evt.Day, Location: evt.Location, Id: metadata.id, Quest: metadata.stream }
    }
});
