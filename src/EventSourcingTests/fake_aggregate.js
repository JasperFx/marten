var mt_transforms = require('mt_transforms');
mt_transforms.snapshot({
    name: 'fake_aggregate',
    $init: function () {
        return { ANames: [], BNames: [], CNames: [], DNames: [] }
    },
    event_a: function (a, e) {
        a.ANames.push(e.Name);
    },
    event_b: function (a, e) {
        a.BNames.push(e.Name);
    },
    event_c: function (a, e) {
        c.ANames.push(e.Name);
    },
    event_d: function (a, e) {
        d.ANames.push(e.Name);
    }
})
