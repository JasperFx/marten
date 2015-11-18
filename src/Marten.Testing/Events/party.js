var mt_events = require('mt_events');

module.exports = mt_events.snapshot({
    timing: inline,

    $init: function(stream) {
        return {Quest: stream.id, Members:[], Day: 0, Location: 'Nowhere'}
    },

    members_joined: function(aggregate, event) {
        aggregate.Members = aggregate.Members.concat(event.Members);
        aggregate.Location = event.Location;
        aggregate.Day = event.Day;
    },

    quest_started: function(aggregate, event) {
        aggregate.Members = aggregate.Members.concat(event.Members);
        aggregate.Location = event.Location;
        aggregate.Day = 0;
    }
});