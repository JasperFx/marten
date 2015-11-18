var mt_transforms = require('./../javascript/mt_transforms');
var expect = require('chai').expect;

describe('mt_transforms', () => {
	describe('Transforms', () => {
		var transformer = mt_transforms.transform({
			name: 'location',
			members_joined: function(e, metadata){
				return {day: e.day, place: e.location};
			},
			members_departed: function(e, metadata){
				return {subject: 'members_departed'};
			}
		});

		it('should always have the type transform', () => {
			expect(transformer.type).to.equal('transform');
		});

		it('should copy the name', () => {
			expect(transformer.name).to.equal('location');
		});

		it('makes the default timing as inline', () => {
			expect(transformer.timing).to.equal('inline');
		});

		it('can use explicit timing', () => {
			var t = mt_transforms.transform({
				name: 'location',
				timing: 'live',
				members_joined: function(e, metadata){
					return {day: e.day, location: e.location};
				}
			});

			expect(t.timing).to.equal('live');
		});

		it('can transform', () => {
			expect(transformer.transform('members_joined', {day: 1, location: "Emond's Field"}))
				.to.deep.equal({day: 1, place: "Emond's Field"});

			expect(transformer.transform('members_departed', {day: 1, location: "Emond's Field"}))
				.to.deep.equal({subject: 'members_departed'});
		});

		it('can derive its usages', () => {
			expect(transformer.usages()).to.deep.equal([
				{timing: 'inline', name: 'location', event_name: 'members_joined', type: 'transform'},
				{timing: 'inline', name: 'location', event_name: 'members_departed', type: 'transform'}
			]);
		});
	});


	describe('Snapshot', () => {
		var snapshot = mt_transforms.snapshot({
			name: 'quest',
			$init: function(){
				return {quest: 'yes', day: 0, members: []}
			},
			members_joined: function(aggregate, e, metadata){
				aggregate.members = aggregate.members.concat(e.members);
			},
			members_departed: function(aggregate, e, metadata){
				aggregate.members = [];
			}

		});

		it('sets the default timing to inline', () => {
			expect(snapshot.timing).to.equal('inline');
		});

		it('should always have the type transform', () => {
			expect(snapshot.type).to.equal('snapshot');
		});

		it('should copy the name', () => {
			expect(snapshot.name).to.equal('quest');
		});

		it('allows you to override the timing', () => {
			var s = mt_transforms.snapshot({
				name: 'quest',
				timing: 'live',
				$init: function(){
					return {quest: 'yes', day: 0, members: []}
				},
				members_joined: function(aggregate, e, metadata){
					aggregate.members = aggregate.members.concat(e.members);
				},
				members_departed: function(aggregate, e, metadata){
					aggregate.members = [];
				}

			});

			expect(s.timing).to.equal('live');
		});

		it('can calculate its usages', () => {
			expect(snapshot.usages()).to.deep.equal([
				{timing: 'inline', name: 'quest', event_name: 'members_joined', type: 'snapshot'},
				{timing: 'inline', name: 'quest', event_name: 'members_departed', type: 'snapshot'}
			]);
		});

		it('can start a transformation', () => {
			var aggregate = snapshot.transform("foo", {}, null, null);

			expect(aggregate).to.deep.equal({quest: 'yes', day: 0, members: []});
		});

		it('can transform an existing aggregate', () => {
			var starting = {};

			var result = snapshot.transform('members_joined', {members: ['Rand', 'Mat']})
		
			expect(result).to.deep.equal({quest: 'yes', day: 0, members: ['Rand', 'Mat']});
		});
	});
});