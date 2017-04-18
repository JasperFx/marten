var Patch = require('./../javascript/mt_patching');
var expect = require('chai').expect;

describe('Patching API', function() {
  it('can set the first level prop', function() {
    var doc = {name: 'Frodo'};

    var patch = {type: 'set', path: 'name', value: 'Bilbo'};

    expect(Patch(doc, patch)).to.deep.equal({name: 'Bilbo'});
  });

  it('can set a deep prop that already exists', function() {
     var doc = {member: {name: 'Frodo'}};
     var patch = {type: 'set', path: 'member.name', value: 'Bilbo'};

     expect(Patch(doc, patch)).to.deep.equal({member: {name: 'Bilbo'}});
  });

  it('can set a deep prop does not already exists', function() {
     var doc = {};
     var patch = {type: 'set', path: 'member.name', value: 'Bilbo'};

     expect(Patch(doc, patch)).to.deep.equal({member: {name: 'Bilbo'}});
  });

  it('can set a 3 deep prop that already exists', function() {
     var doc = {quest: {member: {name: 'Frodo'}}};
     var patch = {type: 'set', path: 'quest.member.name', value: 'Bilbo'};

     expect(Patch(doc, patch)).to.deep.equal({quest: {member: {name: 'Bilbo'}}});
  });

  it('can set a 3 deep prop that does not yet exists', function() {
     var doc = {};
     var patch = {type: 'set', path: 'quest.member.name', value: 'Bilbo'};

     expect(Patch(doc, patch)).to.deep.equal({quest: {member: {name: 'Bilbo'}}});
  });

  it('can increment int first level', function() {
    var doc = {count: 5};
    var patch = {type: 'increment', path: 'count', increment: 1};

    expect(Patch(doc, patch)).to.deep.equal({count: 6});
  });

  it('can increment int first level, non default increment', function() {
    var doc = {count: 5};
    var patch = {type: 'increment', path: 'count', increment: 3};

    expect(Patch(doc, patch)).to.deep.equal({count: 8});
  });

  it('can increment int first level default increment', function() {
    var doc = {count: 5};
    var patch = {type: 'increment', path: 'count'};

    expect(Patch(doc, patch)).to.deep.equal({count: 6});
  });

  it('can increment int 2 deep', function() {
    var doc = {summary: {count: 5}};
    var patch = {type: 'increment', path: 'summary.count'};

    expect(Patch(doc, patch)).to.deep.equal({summary: {count: 6}});
  });

  it('can increment int 3 deep', function() {
    var doc = {region: {summary: {count: 5}}};
    var patch = {type: 'increment', path: 'region.summary.count'};

    expect(Patch(doc, patch)).to.deep.equal({region: {summary: {count: 6}}});
  });

  it('can increment float first level', function() {
    var doc = {count: 5.1};
    var patch = {type: 'increment_float', path: 'count', increment: 1};

    expect(Patch(doc, patch)).to.deep.equal({count: 6.1});
  });

  it('can increment float first level, non default increment', function() {
    var doc = {count: 5.1};
    var patch = {type: 'increment_float', path: 'count', increment: 3.2};

    expect(Patch(doc, patch)).to.deep.equal({count: 8.3});
  });

  it('can increment float first level default increment', function() {
    var doc = {count: 5.1};
    var patch = {type: 'increment_float', path: 'count'};

    expect(Patch(doc, patch)).to.deep.equal({count: 6.1});
  });

  it('can increment float 2 deep', function() {
    var doc = {summary: {count: 5.1}};
    var patch = {type: 'increment_float', path: 'summary.count'};

    expect(Patch(doc, patch)).to.deep.equal({summary: {count: 6.1}});
  });

  it('can increment float 3 deep', function() {
    var doc = {region: {summary: {count: 5.3}}};
    var patch = {type: 'increment_float', path: 'region.summary.count', increment: 1.1};

    expect(Patch(doc, patch)).to.deep.equal({region: {summary: {count: 6.4}}});
  });

  it('can append to a first level array', function() {
    var doc = {numbers: []};

    var patch = {type: 'append', path: 'numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [5]});
  });

  it('can append if not exists to a first level array', function() {
    var doc = {numbers: []};

    var patch = {type: 'append_if_not_exists', path: 'numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [5]});
  });

  it('can append to a first level array with existing elements', function() {
    var doc = {numbers: [3, 4]};

    var patch = {type: 'append', path: 'numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [3, 4, 5]});
  });

  it('can append if not exists to a first level array with existing elements', function() {
    var doc = {numbers: [3, 4]};

    var patch = {type: 'append_if_not_exists', path: 'numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [3, 4, 5]});
  });  

  it('can append if not exists to a first level array with existing element and element count stays the same', function() {
    var doc = {numbers: [3, 4]};

    var patch = {type: 'append_if_not_exists', path: 'numbers', value: 4};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [3, 4]});
  });  

  it('can append to a first level array when the array does not already exist', function() {
    var doc = {};

    var patch = {type: 'append', path: 'numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [5]});
  });

  it('can append if not exists to a first level array when the array does not already exist', function() {
    var doc = {};

    var patch = {type: 'append_if_not_exists', path: 'numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [5]});
  });

  it('can append to a 2nd level array when the array does not already exist', function() {
    var doc = {};

    var patch = {type: 'append', path: 'region.numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({region: {numbers: [5]}});
  });

  it('can append if not exists to a 2nd level array when the array does not already exist', function() {
    var doc = {};

    var patch = {type: 'append_if_not_exists', path: 'region.numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({region: {numbers: [5]}});
  });

  it('can append to a 3rd level array with existing elements', function() {
    var doc = {division: {region: {numbers: [3, 4]}}};

    var patch = {type: 'append', path: 'division.region.numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({division: {region: {numbers: [3, 4, 5]}}});
  });

  it('can append if not exists to a 3rd level array with existing elements', function() {
    var doc = {division: {region: {numbers: [3, 4]}}};

    var patch = {type: 'append_if_not_exists', path: 'division.region.numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({division: {region: {numbers: [3, 4, 5]}}});
  });  

  it('can append if not exists to a 3rd level array with existing elements and the element count stays the same', function() {
    var doc = {division: {region: {numbers: [3, 4]}}};

    var patch = {type: 'append_if_not_exists', path: 'division.region.numbers', value: 4};

    expect(Patch(doc, patch)).to.deep.equal({division: {region: {numbers: [3, 4]}}});
  });   

  it('can append if not exists to an array with complex element', function() {
    var doc = {divisions: [{region: {numbers: [3, 4]}},{region: {numbers: [5, 6]}}]};

    var element = {region: {numbers: [7,8]}}

    var patch = {type: 'append_if_not_exists', path: 'divisions', value: element};

    expect(Patch(doc, patch)).to.deep.equal({divisions: [{region: {numbers: [3, 4]}},{region: {numbers: [5, 6]}},{region: {numbers: [7, 8]}}]});
  });     

  it('can append if not exists to an array with complex existing element and element count stays the same', function() {
    var doc = {divisions: [{region: {numbers: [3, 4]}},{region: {numbers: [5, 6]}}]};

    var element = {region: {numbers: [5,6]}}

    var patch = {type: 'append_if_not_exists', path: 'divisions', value: element};

    expect(Patch(doc, patch)).to.deep.equal({divisions: [{region: {numbers: [3, 4]}},{region: {numbers: [5, 6]}}]});
  });     
  

  it('can rename a prop shallow', function() {
    var doc = {number: 1};

    var patch = {type: 'rename', path: 'number', to: 'value'};

    expect(Patch(doc, patch)).to.deep.equal({value: 1});
  });

  it('can rename a prop 2 deep', function() {
    var doc = {region: {number: 1}};

    var patch = {type: 'rename', path: 'region.number', to: 'value'};

    expect(Patch(doc, patch)).to.deep.equal({region: {value: 1}});
  });

  it('can rename a prop 3 deep', function() {
    var doc = {country: {region: {number: 1}}};

    var patch = {type: 'rename', path: 'country.region.number', to: 'value'};

    expect(Patch(doc, patch)).to.deep.equal({country: {region: {value: 1}}});
  });

  it('can insert into a child collection with default', function() {
    var doc = {numbers: [3, 4]};

    var patch = {type: 'insert', path: 'numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [5, 3, 4]});
  });

  it('can insert not exists into a child collection with default', function() {
    var doc = {numbers: [3, 4]};

    var patch = {type: 'insert_if_not_exists', path: 'numbers', value: 5};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [5, 3, 4]});
  });

  it('can insert not exists into a child collection with existing element', function() {
    var doc = {numbers: [3, 4]};

    var patch = {type: 'insert_if_not_exists', path: 'numbers', value: 3};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [3, 4]});
  });  

  it('can insert into a child collection with designated index', function() {
    var doc = {numbers: [3, 4]};

    var patch = {type: 'insert', path: 'numbers', value: 5, index: 1};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [3, 5, 4]});
  });

  it('can insert not exists into a child collection with designated index', function() {
    var doc = {numbers: [3, 4]};

    var patch = {type: 'insert_if_not_exists', path: 'numbers', value: 5, index: 1};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [3, 5, 4]});
  });  

  it('can insert not exists into a child collection with designated index with existing element', function() {
    var doc = {numbers: [3, 4]};

    var patch = {type: 'insert_if_not_exists', path: 'numbers', value: 4, index: 1};

    expect(Patch(doc, patch)).to.deep.equal({numbers: [3, 4]});
  });    

  it('can insert into a 2 deep child collection with designated index', function() {
    var doc = {region: {numbers: [3, 4]}};

    var patch = {type: 'insert', path: 'region.numbers', value: 5, index: 1};

    expect(Patch(doc, patch)).to.deep.equal({region: {numbers: [3, 5, 4]}});
  });

  it('can insert not exists into a 2 deep child collection with designated index', function() {
    var doc = {region: {numbers: [3, 4]}};

    var patch = {type: 'insert_if_not_exists', path: 'region.numbers', value: 5, index: 1};

    expect(Patch(doc, patch)).to.deep.equal({region: {numbers: [3, 5, 4]}});
  });  

  it('can insert not exists into a 2 deep child collection with designated index with existing element', function() {
    var doc = {region: {numbers: [3, 4]}};

    var patch = {type: 'insert_if_not_exists', path: 'region.numbers', value: 4, index: 1};

    expect(Patch(doc, patch)).to.deep.equal({region: {numbers: [3, 4]}});
  });

  it('can insert if not exists to an array with complex element', function() {
    var doc = {divisions: [{region: {numbers: [3, 4]}},{region: {numbers: [5, 6]}}]};

    var element = {region: {numbers: [7,8]}}

    var patch = {type: 'insert_if_not_exists', path: 'divisions', value: element};

    expect(Patch(doc, patch)).to.deep.equal({divisions: [{region: {numbers: [7, 8]}},{region: {numbers: [3, 4]}},{region: {numbers: [5, 6]}}]});
  });     

  it('can insert if not exists to an array with complex existing element and element count stays the same', function() {
    var doc = {divisions: [{region: {numbers: [3, 4]}},{region: {numbers: [5, 6]}}]};

    var element = {region: {numbers: [3, 4]}}

    var patch = {type: 'insert_if_not_exists', path: 'divisions', value: element};

    expect(Patch(doc, patch)).to.deep.equal({divisions: [{region: {numbers: [3, 4]}},{region: {numbers: [5, 6]}}]});
  });           

  it('can remove from array', function() {
    var doc = {items: ['foo', 'bar']};

    var patch = {type: 'remove', path: 'items', value: 'bar'};

    expect(Patch(doc, patch)).to.deep.equal({items: ['foo']});
  });

  it('can remove multiple occurances from array', function() {
    var doc = {items: [0, 1, 0, 1, 0, 1]};

    var patch = {type: 'remove', path: 'items', value: 1, action: 1};

    expect(Patch(doc, patch)).to.deep.equal({items: [0, 0, 0]});
  });

  it('can remove from array when item not present', function() {
    var doc = {items: ['foo']};

    var patch = {type: 'remove', path: 'items', value: 'bar'};

    expect(Patch(doc, patch)).to.deep.equal({items: ['foo']});
  });

  it('can remove from array when array does not exist', function() {
    var doc = {};

    var patch = {type: 'remove', path: 'items', value: 'bar'};

    expect(Patch(doc, patch)).to.deep.equal({});
  });

  it('can remove object from array', function() {
    var doc = {children: [{name: 'first'}, {name: 'second'}, {name: 'third'}]};

    var patch = {type: 'remove', path: 'children', value: {name: 'second'}};

    expect(Patch(doc, patch)).to.deep.equal({children: [{name: 'first'}, {name: 'third'}]});
  });

  it('can delete prop shallow', function() {
    var doc = {first: 'foo', second: 'bar', third: 'baz'};

    var patch = {type: 'delete', path: 'second'};

    expect(Patch(doc, patch)).to.deep.equal({first: 'foo', third: 'baz'});
  });

  it('can delete prop deep', function() {
    var doc = {top: {middle: {bottom: {first: 'foo', second: 'bar'}}}};

    var patch = {type: 'delete', path: 'top.middle.bottom.first'};

    expect(Patch(doc, patch)).to.deep.equal({top: {middle: {bottom: {second: 'bar'}}}});
  });

  it('can delete missing prop', function() {
    var doc = {first: 'foo', second: 'bar'};

    var patch = {type: 'delete', path: 'third'};

    expect(Patch(doc, patch)).to.deep.equal({first: 'foo', second: 'bar'});
  });

  it('can duplicate prop', function() {
    var doc = {first: 'foo', second: 'bar'};

    var patch = {type: 'duplicate', path: 'first', targets: ['third']};

    expect(Patch(doc, patch)).to.deep.equal({first: 'foo', second: 'bar', third: 'foo'});
  });

  it('can duplicate prop to multiple targets', function() {
    var doc = {first: {items: ['foo', 'bar']}, second: {}, third: {}};

    var patch = {type: 'duplicate', path: 'first.items', targets: ['second.parts','third.bits']};

    expect(Patch(doc, patch)).to.deep.equal({first: {items: ['foo', 'bar']}, second: {parts: ['foo', 'bar']}, third: {bits: ['foo', 'bar']}});
  });

  it('can duplicate prop to missing and partial parents', function() {
    var doc = {items: [3, 5, 8, 13], first: null, second: null};

    var patch = {type: 'duplicate', path: 'items', targets: ['first.items','second.inner.items']};

    expect(Patch(doc, patch)).to.deep.equal({items: [3, 5, 8, 13], first: {items: [3, 5, 8, 13]}, second: {inner: {items: [3, 5, 8, 13]}}});
  });
});
