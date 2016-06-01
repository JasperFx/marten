var Patch = require('./../javascript/mt_patching');
var expect = require('chai').expect;

describe('Patching API', () => {
  it('can set the first level prop', () => {
    var doc = {name: 'Frodo'};
    
    var patch = {type: 'set', path: 'name', value: 'Bilbo'};
    
    expect(Patch(doc, patch)).to.deep.equal({name: 'Bilbo'}); 
  });
  
  it('can set a deep prop that already exists', () => {
     var doc = {member: {name: 'Frodo'}}; 
     var patch = {type: 'set', path: 'member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({member: {name: 'Bilbo'}}); 
  });
  
  it('can set a deep prop does not already exists', () => {
     var doc = {}; 
     var patch = {type: 'set', path: 'member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({member: {name: 'Bilbo'}}); 
  });
  
  it('can set a 3 deep prop that already exists', () => {
     var doc = {quest: {member: {name: 'Frodo'}}}; 
     var patch = {type: 'set', path: 'quest.member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({quest: {member: {name: 'Bilbo'}}}); 
  });
  
  it('can set a 3 deep prop that does not yet exists', () => {
     var doc = {}; 
     var patch = {type: 'set', path: 'quest.member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({quest: {member: {name: 'Bilbo'}}}); 
  });
});